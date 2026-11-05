using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

// nTuneUpOptions packs eight 4-bit option levels; the combined level is capped at MAX_TUNEUPLEVEL.
public sealed partial class PlayerStore
{
    private const int LunariteA = 500001;
    private const int LunariteB = 500002;
    private const int LunariteC = 500003;

    // Returns the native tune-up result code. The upgraded option is server-side RNG unless overridden.
    public byte TryTuneUp(int pid, byte cell, int discountPercent, out byte option, out int oldOptions, out int newOptions,
        int? optionOverride = null)
    {
        option = 0; oldOptions = newOptions = 0;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return 1;
            var item = ItemAt(list, InventoryType.Main, cell);
            if (item == null) return 1;
            var data = _itemCatalog.Get(item.ItemId);
            if (!IsTuneable(data)) return 2;
            if (data.Repairable && item.Durability < MaxDurability) return 6;
            if (_serverEvents.BlocksPcRoomItem(data)) return 9; // RESULTTYPE_TUNEUP_PCROOM_ITEM
            oldOptions = item.TuneUpOptions;
            int packed = oldOptions;
            int total = Enumerable.Range(0, 8).Sum(i => OptionLevel(packed, i));
            if (total >= MaxTuneUpLevel) return 3;
            var eligibleOptions = data.TuneupOptionCaps
                .Select((cap, index) => (cap, index))
                .Where(x => x.cap > OptionLevel(packed, x.index))
                .Select(x => x.index).ToList();
            if (eligibleOptions.Count == 0) return 3;

            int nextLevel = total + 1;
            var (materialId, materialPerLevel) = TuneUpMaterial(data);
            float tuneUpRate = _economyRates.ForPid(pid).TuneUp;
            int materialCount = checked((int)(nextLevel * materialPerLevel * tuneUpRate));
            if (materialId != 0 && MainCount(list, materialId) < materialCount)
                return 4;
            var progress = ProgressFor(pid);
            // HuxleyTuneupMenu.MakeMaterialList: city-license group 0 gives (1.0 - DiscountAmount*0.01),
            // multiplied by the faction tune-up rate and truncated to int.
            int discount = Math.Clamp(discountPercent, 0, 100);
            int fee = checked((int)(nextLevel * data.TuneupFee * (1.0f - discount * 0.01f) * tuneUpRate));
            if (progress.Money < fee) return 5;

            int selected = optionOverride.HasValue && eligibleOptions.Contains(optionOverride.Value)
                ? optionOverride.Value
                : eligibleOptions[Random.Shared.Next(eligibleOptions.Count)];
            option = (byte)selected;
            newOptions = oldOptions + (1 << (option * 4));
            if (materialId != 0) ConsumeLunarites(pid, list, materialId, materialCount);
            progress.Money -= fee;
            item.TuneUpOptions = newOptions;
            SaveInventory(); SaveItemTotals(); SaveProgress();
            return 0;
        }
    }

    // Native RESULTTYPE_RESETTUNEUP_RESULT: 0 OK, 1 ERROR, 2 missing use item,
    // 3 missing destination, 4 destination has no tune-up, 5 selected option is zero,
    // 6 inventory blocked, 7 item blocked, 8 destination is not tuneable.
    public byte TryResetTuneUp(int pid, byte useCell, byte targetCell, byte resetType, byte option,
        out int oldOptions, out int newOptions)
    {
        oldOptions = newOptions = 0;
        if (resetType > 1 || option >= 8) return 1;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return 2;
            var use = ItemAt(list, InventoryType.Main, useCell);
            var target = ItemAt(list, InventoryType.Main, targetCell);
            if (use == null) return 2;
            if (target == null) return 3;
            if (use == target) return 7;
            var tool = _itemCatalog.Get(use.ItemId);
            if (tool == null || (resetType == 1 ? !tool.ResetTuneupAll : !tool.ResetTuneupPart)) return 2;
            if (!IsTuneable(_itemCatalog.Get(target.ItemId))) return 8;
            oldOptions = target.TuneUpOptions;
            if (oldOptions == 0) return 4;
            if (resetType == 0 && OptionLevel(oldOptions, option) == 0) return 5;

            // A partial reset (item 672002) lowers the selected option by one level only.
            newOptions = resetType == 1 ? 0 : oldOptions - (1 << (option * 4));
            if (--use.Count <= 0) list.Remove(use);
            ReduceTotal(pid, use.ItemId, 1);
            target.TuneUpOptions = newOptions;
            SaveItemTotals(); SaveInventory();
            return 0;
        }
    }

    private static bool IsTuneable([NotNullWhen(true)] ItemCatalog.Item? data) =>
        data != null && (data.TuneupFee != 0
            || data.TuneupLunaritesA + data.TuneupLunaritesB + data.TuneupLunaritesC != 0);

    private static int OptionLevel(int packed, int option) => (packed >> (option * 4)) & 0xF;

    // The first non-zero lunarite grade is the material; its value is the per-level count.
    private (int ItemId, int PerLevel) TuneUpMaterial(ItemCatalog.Item data)
    {
        if (data.TuneupLunaritesA > 0) return (LunariteA, data.TuneupLunaritesA);
        if (data.TuneupLunaritesB > 0) return (LunariteB, data.TuneupLunaritesB);
        if (data.TuneupLunaritesC > 0) return (LunariteC, data.TuneupLunaritesC);
        return (0, data.TuneupLunaritesC);
    }

    private void ConsumeLunarites(int pid, List<GenericItem> list, int materialId, int count)
    {
        int remaining = count;
        foreach (var slot in list.Where(x => x.InventoryIndex == InventoryType.Main && x.ItemId == materialId).ToList())
        {
            int take = Math.Min(remaining, slot.Count);
            slot.Count -= take;
            remaining -= take;
            if (slot.Count == 0) list.Remove(slot);
            if (remaining == 0) break;
        }
        ReduceTotal(pid, materialId, count);
    }
}
