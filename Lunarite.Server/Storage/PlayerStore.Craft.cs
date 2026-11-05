using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    public enum CraftResultCode : byte
    {
        Success = 0, Error = 1, SuccessAndLearnRecipe = 2, Break = 3, Fail = 4,
        NotLearnedRecipe = 5, IsNotMaterial = 6, NotEnoughMoney = 7,
        NotEnoughMaterial = 8, DurabilityError = 9, InventoryFull = 10,
        NotFoundResultItem = 11, InventoryBlocked = 12, ItemBlocked = 13
    }

    // Validates on a cloned inventory, then commits materials, money, totals, and output together.
    // MAIN cell 0xFF selects a compatible stack or the first free cell.
    public CraftResultCode TryCraft(int pid, CraftCatalog.Recipe recipe, byte requestedCell,
        int outputItemId, out List<byte> changedCells, out GenericItem? output, out long money)
    {
        changedCells = [];
        output = null;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            money = progress.Money;
            var outputData = _itemCatalog.Get(outputItemId);
            if (outputData == null) return CraftResultCode.NotFoundResultItem;
            if (progress.Money < recipe.CraftCost) return CraftResultCode.NotEnoughMoney;
            if (!_inventory.TryGetValue(pid, out var current)) return CraftResultCode.NotEnoughMaterial;

            var work = current.Select(Clone).ToList();
            if (!TakeCraftMaterials(work, recipe, changedCells)) return CraftResultCode.NotEnoughMaterial;
            var target = ResolveCraftTarget(pid, work, requestedCell, outputItemId, outputData);
            if (target == null) return CraftResultCode.InventoryFull;

            target.Count++;
            ApplyPickupBinding(target, outputData);
            StartPossessedLimit(target, outputData, UnixNow());
            changedCells.Add(target.InventoryCell);
            progress.Money -= recipe.CraftCost;
            _inventory[pid] = work;
            if (!_itemTotals.TryGetValue(pid, out var totals)) totals = _itemTotals[pid] = [];
            foreach (var material in recipe.Materials)
            {
                int next = totals.GetValueOrDefault(material.ItemId) - material.Count;
                if (next <= 0) totals.Remove(material.ItemId); else totals[material.ItemId] = next;
            }
            totals[outputItemId] = totals.GetValueOrDefault(outputItemId) + 1;
            output = Clone(target);
            money = progress.Money;
            changedCells = changedCells.Distinct().ToList();
            SaveItemTotals(); SaveInventory(); SaveProgress();
            // CraftMessage maps BREAK to the ordinary MakeItemIndex result and SUCCESS to
            // LuckyItemIndex; the native names are counterintuitive.
            return recipe.LuckyItemIndex > 0 && outputItemId == recipe.LuckyItemIndex
                ? CraftResultCode.Success : CraftResultCode.Break;
        }
    }

    private static bool TakeCraftMaterials(List<GenericItem> work, CraftCatalog.Recipe recipe, List<byte> changedCells)
    {
        foreach (var material in recipe.Materials)
        {
            if (MainCount(work, material.ItemId) < material.Count) return false;
            int need = material.Count;
            foreach (var slot in work.Where(i => i.InventoryIndex == InventoryType.Main && i.ItemId == material.ItemId)
                .OrderBy(i => i.InventoryCell).ToList())
            {
                int take = Math.Min(need, slot.Count);
                slot.Count -= take;
                need -= take;
                changedCells.Add(slot.InventoryCell);
                if (slot.Count == 0) work.Remove(slot);
                if (need == 0) break;
            }
        }
        return true;
    }

    // Returns the MAIN stack the output goes into (adding a new empty one if needed), or null when full.
    private GenericItem? ResolveCraftTarget(int pid, List<GenericItem> work, byte requestedCell,
        int outputItemId, ItemCatalog.Item outputData)
    {
        GenericItem? target;
        if (requestedCell == 0xFF)
        {
            target = work.FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.ItemId == outputItemId
                && i.Count < outputData.MaxCollapseCount);
            if (target != null) return target;
            int free = FindEmptyCell(work, MainCapacity(pid));
            if (free < 0) return null;
            target = NewMainStack((byte)free, outputItemId, outputData);
            work.Add(target);
            return target;
        }

        if (requestedCell >= MainCapacity(pid)) return null;
        target = ItemAt(work, InventoryType.Main, requestedCell);
        if (target == null)
        {
            target = NewMainStack(requestedCell, outputItemId, outputData);
            work.Add(target);
            return target;
        }
        return target.ItemId != outputItemId || target.Count >= outputData.MaxCollapseCount ? null : target;
    }
}
