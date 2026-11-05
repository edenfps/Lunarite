using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Lunarite.Packets.Common;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    // The server copy of the client's GenericItem (HuxleyItemSystem.uc), one per inventory slot.
    // Saved inventories predate the bInventoryIndex/bInventoryCell names, hence the JSON names.
    public sealed class GenericItem
    {
        [JsonPropertyName("Inven")] public byte InventoryIndex { get; set; }

        [JsonPropertyName("Cell")] public byte InventoryCell { get; set; }

        public int ItemId { get; set; }

        public int Count { get; set; } = 1;

        public byte State { get; set; }

        public int LimitTime { get; set; }

        public int TuneUpOptions { get; set; }

        public int Durability { get; set; } = 100;

        public int Skill { get; set; }

        public int Skill1 { get; set; }
    }

    public void AddItems(int pid, IEnumerable<(int item, int num)> items)
    {
        lock (_lock)
        {
            if (!_itemTotals.TryGetValue(pid, out var totals)) totals = _itemTotals[pid] = new();
            foreach (var (item, num) in items)
                if (item != 0 && num != 0) totals[item] = totals.GetValueOrDefault(item) + num;
            SaveItemTotals();
        }
    }

    public List<(int item, int num)> GetItems(int pid)
    {
        lock (_lock)
        {
            if (!_itemTotals.TryGetValue(pid, out var totals)) return new();
            return totals.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }

    // SendQuestReduceItem: quantity 0 removes every copy. All-or-nothing across both stores.
    public bool TryReduceItem(int pid, int itemId, int quantity, byte? inventory = null)
    {
        lock (_lock)
        {
            if (!_itemTotals.TryGetValue(pid, out var totals) || !totals.TryGetValue(itemId, out int total))
                return false;
            bool Matches(GenericItem x) => x.ItemId == itemId && (!inventory.HasValue || x.InventoryIndex == inventory.Value);

            int available = total;
            if (inventory.HasValue)
            {
                if (!_inventory.TryGetValue(pid, out var selectedSlots)) return false;
                available = selectedSlots.Where(Matches).Sum(x => x.Count);
            }
            int remove = quantity == 0 ? available : quantity;
            if (remove <= 0 || total < remove || available < remove) return false;

            bool hasSlots = _inventory.TryGetValue(pid, out var slots);
            if (hasSlots && slots!.Where(Matches).Sum(x => x.Count) < remove) return false;

            int remaining = total - remove;
            if (remaining == 0) totals.Remove(itemId); else totals[itemId] = remaining;
            if (hasSlots) TakeFromStacks(slots!, slots!.Where(Matches).ToList(), remove);
            SaveItemTotals();
            SaveInventory();
            return true;
        }
    }

    // A present PID key marks the inventory as initialized; an empty list is a valid state.
    public List<GenericItem> GetInventory(int pid, Func<List<GenericItem>> buildInitial)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list))
            {
                list = _inventory[pid] = buildInitial();
                SaveInventory();
            }

            if (RepairLegacyStarterSkills(list))
            {
                SaveInventory();
                _logger.LogInformation("[Store] migrated PID {0} to the retail tutorial socket-skill loadout.", pid);
            }
            return list;
        }
    }

    public List<GenericItem> GetInventorySnapshot(int pid, Func<List<GenericItem>> buildInitial)
    {
        lock (_lock) return GetInventory(pid, buildInitial).Select(Clone).ToList();
    }

    // Retail op76: plain moves are mirrored client-side by op77 result 0. Split/merge needs an
    // authoritative op81/op82 refresh and result 31, which the client treats as success without
    // performing its local whole-slot swap.
    public bool TryMoveWithinInventory(int pid, byte inven, byte sourceCell, byte targetCell,
        int requestedCount, out bool authoritativeRefresh, out GenericItem? sourceAfter, out GenericItem? targetAfter)
    {
        authoritativeRefresh = false;
        sourceAfter = null;
        targetAfter = null;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list) || sourceCell == targetCell) return false;
            // The client only sends op76 within MAIN, BANK and the weapon slots, and never past the
            // inventory's capacity: cells beyond it are storage the client cannot show.
            int capacity = inven switch
            {
                InventoryType.Main => MainCapacity(pid),
                InventoryType.Bank => BankCapacity(pid),
                InventoryType.Weapon => 4,
                _ => 0
            };
            if (sourceCell >= capacity || targetCell >= capacity) return false;
            var source = ItemAt(list, inven, sourceCell);
            if (source == null) return false;
            var target = ItemAt(list, inven, targetCell);
            int moveCount = requestedCount <= 0 ? source.Count : requestedCount;
            if (moveCount <= 0 || moveCount > source.Count) return false;

            // The client sends op76 for any drop onto an occupied cell, so only stackable items in
            // the same state merge, and never past MaxCollapseCount; anything else is a plain swap.
            int stackRoom = target != null && CanStack(source, target)
                ? (_itemCatalog.Get(source.ItemId)?.MaxCollapseCount ?? 1) - target.Count : 0;
            if (stackRoom > 0)
            {
                moveCount = Math.Min(moveCount, stackRoom);
                target!.Count += moveCount;
                source.Count -= moveCount;
                if (source.Count == 0) list.Remove(source);
                authoritativeRefresh = true;
            }
            else if (target == null && moveCount < source.Count)
            {
                source.Count -= moveCount;
                target = Clone(source);
                target.InventoryCell = targetCell;
                target.Count = moveCount;
                list.Add(target);
                authoritativeRefresh = true;
            }
            else
            {
                if (target != null) target.InventoryCell = sourceCell;
                source.InventoryCell = targetCell;
            }

            sourceAfter = list.Where(i => i.InventoryIndex == inven && i.InventoryCell == sourceCell)
                .Select(Clone).FirstOrDefault();
            targetAfter = list.Where(i => i.InventoryIndex == inven && i.InventoryCell == targetCell)
                .Select(Clone).FirstOrDefault();
            SaveInventory();
            return true;
        }
    }

    // Durability only tells stacks apart for repairable items; consumable stacks carry 0 or the
    // GenericItem default 100 depending on how they were created, and must still merge.
    private bool CanStack(GenericItem source, GenericItem target) =>
        target.ItemId == source.ItemId
        && _itemCatalog.Get(source.ItemId) is { MaxCollapseCount: > 1 } definition
        && target.State == source.State && target.LimitTime == source.LimitTime
        && target.TuneUpOptions == source.TuneUpOptions
        && (!definition.Repairable || target.Durability == source.Durability)
        && target.Skill == source.Skill && target.Skill1 == source.Skill1;

    public bool TryDropItem(int pid, byte inven, byte cell, int requestedCount, out GenericItem? remaining)
    {
        remaining = null;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var item = ItemAt(list, inven, cell);
            if (item == null || requestedCount <= 0 || requestedCount > item.Count) return false;
            item.Count -= requestedCount;
            if (item.Count == 0) list.Remove(item);
            else remaining = Clone(item);
            ReduceTotal(pid, item.ItemId, requestedCount);
            SaveItemTotals(); SaveInventory();
            return true;
        }
    }

    public bool ArrangeMainInventory(int pid, out List<byte> oldCells, out List<GenericItem> arranged)
    {
        oldCells = [];
        arranged = [];
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var main = list.Where(i => i.InventoryIndex == InventoryType.Main).OrderBy(i => i.InventoryCell).ToList();
            oldCells = main.Select(i => i.InventoryCell).ToList();
            for (int cell = 0; cell < main.Count; cell++) main[cell].InventoryCell = (byte)cell;
            arranged = main.Select(Clone).ToList();
            SaveInventory();
            return true;
        }
    }

    // Removes the items at the given MAIN cells (transfer/escrow). Nothing changes if any cell is
    // empty, duplicated, bound, or holds a PC-room item.
    public bool TryRemoveMainItems(int pid, IEnumerable<byte> cells, out List<GenericItem> removed) =>
        TryRemoveMainItems(pid, cells, out removed, out _);

    public bool TryRemoveMainItems(int pid, IEnumerable<byte> cells, out List<GenericItem> removed,
        out bool boundItem) =>
        TryRemoveMainItems(pid, cells, out removed, out boundItem, out _);

    public bool TryRemoveMainItems(int pid, IEnumerable<byte> cells, out List<GenericItem> removed,
        out bool boundItem, out bool cannotEnclose)
    {
        removed = [];
        boundItem = false;
        cannotEnclose = false;
        var wanted = cells.Where(c => c != byte.MaxValue).ToList();
        if (wanted.Count != wanted.Distinct().Count()) return false;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return wanted.Count == 0;
            foreach (byte cell in wanted)
            {
                var item = ItemAt(list, InventoryType.Main, cell);
                if (item == null) { removed = []; return false; }
                if ((item.State & ItemStateBindTag) != 0) { removed = []; boundItem = true; return false; }
                // HuxleyMsgListMenu.CheckPCRoomItem -> TRANSMITRET_CANT_ENCLOSE (13).
                if (_itemCatalog.Get(item.ItemId)?.ItemSalesCategory == 1)
                { removed = []; cannotEnclose = true; return false; }
                removed.Add(Clone(item));
            }
            foreach (var item in removed)
            {
                list.RemoveAll(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == item.InventoryCell);
                ReduceTotal(pid, item.ItemId, item.Count);
            }
            SaveItemTotals(); SaveInventory();
            return true;
        }
    }

    public void RestoreMainItems(int pid, IEnumerable<GenericItem> items)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) list = _inventory[pid] = [];
            if (!_itemTotals.TryGetValue(pid, out var totals)) totals = _itemTotals[pid] = [];
            foreach (var item in items)
            {
                if (list.Any(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == item.InventoryCell)) continue;
                list.Add(Clone(item));
                totals[item.ItemId] = totals.GetValueOrDefault(item.ItemId) + item.Count;
            }
            SaveItemTotals(); SaveInventory();
        }
    }

    // Deposits into the first free MAIN cell; the returned copy is the GenericItem sent in op204.
    public bool TryAddMainItem(int pid, GenericItem source, out GenericItem deposited)
    {
        deposited = new();
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) list = _inventory[pid] = [];
            int free = FindEmptyCell(list, MainCapacity(pid));
            if (free < 0) return false;
            deposited = Clone(source);
            deposited.InventoryIndex = InventoryType.Main;
            deposited.InventoryCell = (byte)free;
            list.Add(deposited);
            AddTotal(pid, deposited.ItemId, deposited.Count);
            SaveItemTotals(); SaveInventory();
            return true;
        }
    }

    public void RemoveMainItem(int pid, byte cell)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return;
            list.RemoveAll(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == cell);
            SaveInventory();
        }
    }

    private static GenericItem? ItemAt(List<GenericItem> list, byte inven, byte cell) =>
        list.FirstOrDefault(i => i.InventoryIndex == inven && i.InventoryCell == cell);

    // Takes count from the given stacks in order, removing any that empty.
    private static void TakeFromStacks(List<GenericItem> list, List<GenericItem> stacks, int count,
        List<byte>? touchedCells = null)
    {
        foreach (var slot in stacks)
        {
            touchedCells?.Add(slot.InventoryCell);
            int take = Math.Min(count, slot.Count);
            slot.Count -= take;
            count -= take;
            if (slot.Count <= 0) list.Remove(slot);
            if (count == 0) break;
        }
    }
}
