using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    // Quest EnterRequestItems are consumed from InventoryType.Main, the inventory the client gate checks.
    public bool HasQuestEntryItems(int pid, IEnumerable<(int ItemId, int Quantity)> requirements)
    {
        var required = RequiredQuantities(requirements);
        if (required.Count == 0) return true;
        lock (_lock) return HasRequiredItems(pid, required);
    }

    public bool TryConsumeQuestEntryItems(int pid,
        IEnumerable<(int ItemId, int Quantity)> requirements, out List<byte> changedCells)
    {
        bool result = TryConsumeQuestEntryItems([pid], requirements, out var allChanged);
        changedCells = allChanged.GetValueOrDefault(pid, []);
        return result;
    }

    // Squad-wide and all-or-nothing: no member is charged unless every member has the items.
    public bool TryConsumeQuestEntryItems(IReadOnlyCollection<int> pids,
        IEnumerable<(int ItemId, int Quantity)> requirements,
        out Dictionary<int, List<byte>> changedByPid)
    {
        changedByPid = [];
        var required = RequiredQuantities(requirements);
        if (required.Count == 0) return true;

        lock (_lock)
        {
            if (!pids.Distinct().All(pid => HasRequiredItems(pid, required))) return false;

            foreach (int pid in pids.Distinct())
            {
                var totals = _itemTotals[pid];
                var slots = _inventory[pid];
                var changedCells = changedByPid[pid] = [];
                foreach (var (itemId, quantity) in required)
                {
                    int remainingTotal = totals[itemId] - quantity;
                    if (remainingTotal == 0) totals.Remove(itemId); else totals[itemId] = remainingTotal;
                    var stacks = slots.Where(x => x.InventoryIndex == InventoryType.Main && x.ItemId == itemId).ToList();
                    TakeFromStacks(slots, stacks, quantity, changedCells);
                }
            }
            SaveItemTotals();
            SaveInventory();
            return true;
        }
    }

    public bool TryAddQuestItems(int pid, IEnumerable<(int item, int num)> items,
        out List<GenericItem> inserted)
    {
        return TryExchangeQuestItems(pid, [], items, out inserted, out _);
    }

    // Rally-end pickups: inserts as many units of each line as MAIN can hold instead of failing the
    // batch, so the client can be told what is left (RESULTTYPE_ITEM_INSERT_PROTION). placed[i] is
    // the quantity line i got; unknown items place nothing.
    public bool TryAddQuestItemsUpTo(int pid, IReadOnlyList<(int item, int num)> items,
        out List<GenericItem> inserted, out int[] placed)
    {
        inserted = [];
        placed = new int[items.Count];
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var work = list.Select(Clone).ToList();
            var totals = _itemTotals.TryGetValue(pid, out var existingTotals)
                ? new Dictionary<int, int>(existingTotals) : [];
            int capacity = MainCapacity(pid);
            var used = work.Where(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell < capacity)
                .Select(i => i.InventoryCell).ToHashSet();
            var changed = new Dictionary<byte, GenericItem>();
            int now = UnixNow();
            for (int i = 0; i < items.Count; i++)
            {
                var (item, quantity) = items[i];
                if (item <= 0 || quantity <= 0 || _itemCatalog.Get(item) == null) continue;
                placed[i] = StackIntoMain(pid, work, used, changed, item, quantity, capacity, now);
                if (placed[i] > 0) totals[item] = totals.GetValueOrDefault(item) + placed[i];
            }
            if (changed.Count == 0) return true;
            _itemTotals[pid] = totals;
            _inventory[pid] = work;
            inserted = changed.Values.OrderBy(x => x.InventoryCell).Select(Clone).ToList();
            SaveItemTotals();
            SaveInventory();
            return true;
        }
    }

    // Discards quest-carried items before granting rewards, all on one cloned inventory so a reward
    // can reuse a freed cell. RemoveItems are item IDs: every carried copy is discarded.
    public bool TryExchangeQuestItems(int pid, IEnumerable<int> removeItemIds,
        IEnumerable<(int item, int num)> items, out List<GenericItem> inserted, out List<byte> changedCells)
    {
        inserted = [];
        changedCells = [];
        var batch = items.ToList();
        if (batch.Any(x => x.item <= 0 || x.num <= 0 || _itemCatalog.Get(x.item) == null)) return false;
        var removals = removeItemIds.Where(x => x > 0).Distinct().ToHashSet();
        if (batch.Count == 0 && removals.Count == 0) return true;

        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var work = list.Select(Clone).ToList();
            var totals = _itemTotals.TryGetValue(pid, out var existingTotals)
                ? new Dictionary<int, int>(existingTotals) : [];
            DiscardQuestItems(work, totals, removals, changedCells);

            int capacity = MainCapacity(pid);
            var used = work.Where(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell < capacity)
                .Select(i => i.InventoryCell).ToHashSet();
            var changed = new Dictionary<byte, GenericItem>();
            int now = UnixNow();
            foreach (var (item, quantity) in batch)
                if (StackIntoMain(pid, work, used, changed, item, quantity, capacity, now) != quantity) return false;

            foreach (var (item, num) in batch)
                totals[item] = totals.GetValueOrDefault(item) + num;
            _itemTotals[pid] = totals;
            _inventory[pid] = work;
            inserted = changed.Values.OrderBy(x => x.InventoryCell).Select(Clone).ToList();
            changedCells.AddRange(changed.Keys);
            changedCells = changedCells.Distinct().Order().ToList();
            SaveItemTotals();
            SaveInventory();
            return true;
        }
    }

    private static Dictionary<int, int> RequiredQuantities(IEnumerable<(int ItemId, int Quantity)> requirements) =>
        requirements
            .Where(x => x.ItemId > 0 && x.Quantity > 0)
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

    private bool HasRequiredItems(int pid, Dictionary<int, int> required)
    {
        if (!_itemTotals.TryGetValue(pid, out var totals) || !_inventory.TryGetValue(pid, out var slots)) return false;
        return required.All(x => totals.GetValueOrDefault(x.Key) >= x.Value && MainCount(slots, x.Key) >= x.Value);
    }

    private static void DiscardQuestItems(List<GenericItem> work, Dictionary<int, int> totals,
        HashSet<int> itemIds, List<byte> changedCells)
    {
        foreach (int itemId in itemIds)
        {
            foreach (var slot in work.Where(x => x.ItemId == itemId).ToList())
            {
                if (slot.InventoryIndex == InventoryType.Main) changedCells.Add(slot.InventoryCell);
                work.Remove(slot);
            }
            totals.Remove(itemId);
        }
    }

    // Tops up existing MAIN stacks (only when every stacked copy is already tracked in the totals),
    // then opens new stacks in free cells. Returns the quantity placed, short when MAIN runs out of room.
    private int StackIntoMain(int pid, List<GenericItem> work, HashSet<byte> used,
        Dictionary<byte, GenericItem> changed, int itemId, int quantity, int capacity, int now)
    {
        int remaining = quantity;
        var itemData = _itemCatalog.Get(itemId);
        int maxStack = Math.Max(1, itemData?.MaxCollapseCount ?? quantity);
        bool allExistingStacksTracked = _itemTotals.TryGetValue(pid, out var trackedTotals)
            && trackedTotals.GetValueOrDefault(itemId) >= MainCount(work, itemId);
        foreach (var stack in work.Where(i => allExistingStacksTracked
                      && i.InventoryIndex == InventoryType.Main && i.ItemId == itemId
                     && i.InventoryCell < capacity && i.Count < maxStack).OrderBy(i => i.InventoryCell))
        {
            int add = Math.Min(remaining, maxStack - stack.Count);
            stack.Count += add;
            ApplyPickupBinding(stack, itemData);
            StartPossessedLimit(stack, itemData, now);
            remaining -= add;
            changed[stack.InventoryCell] = stack;
            if (remaining == 0) break;
        }
        while (remaining > 0)
        {
            int free = Enumerable.Range(0, capacity).FirstOrDefault(x => !used.Contains((byte)x), -1);
            if (free < 0) return quantity - remaining;
            int count = Math.Min(remaining, maxStack);
            var entry = NewMainStack((byte)free, itemId, itemData, count);
            ApplyPickupBinding(entry, itemData);
            StartPossessedLimit(entry, itemData, now);
            work.Add(entry);
            used.Add((byte)free);
            changed[(byte)free] = entry;
            remaining -= count;
        }
        return quantity;
    }
}
