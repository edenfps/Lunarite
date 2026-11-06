using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    // Quest op60 carries four WEAPON and five ARMOR durability triples. Durability is a 0..100
    // percentage; anything else is a displaced byte and is ignored.
    public int UpdateDurability(int pid, IEnumerable<(byte inven, byte cell, byte durability)> updates)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return 0;
            int changed = 0;
            foreach (var (inven, cell, durability) in updates)
            {
                if ((inven != InventoryType.Weapon && inven != InventoryType.Armor) || durability > MaxDurability) continue;
                var item = ItemAt(list, inven, cell);
                if (item == null) continue;
                item.Durability = durability;
                changed++;
            }
            if (changed != 0) SaveInventory();
            return changed;
        }
    }

    // Opt-in native Type-4 backup parser: op60 armor slots and positive weapon values are genuine
    // per-slot results. An unused weapon reports zero (indistinguishable from broken), so zero is
    // ignored, and since results cannot repair gear, increases are ignored too.
    public int UpdateNativeQuestDurability(int pid,
        IEnumerable<(byte inven, byte cell, byte durability)> updates)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return 0;
            int changed = 0;
            foreach (var (inven, cell, durability) in updates)
            {
                bool genuine = (inven == InventoryType.Weapon && cell < 4 && durability > 0)
                    || (inven == InventoryType.Armor && cell < 5);
                if (!genuine || durability > MaxDurability) continue;
                var item = ItemAt(list, inven, cell);
                if (item == null || durability >= item.Durability) continue;
                // As in UpdateQuestDurabilityFromBoots: an unpatched client never loaded the gear.
                if (durability == 0 && item.Durability > MaxDurability / 2) continue;
                item.Durability = durability;
                changed++;
            }
            if (changed != 0) SaveInventory();
            return changed;
        }
    }

    // The cooked quest script decodes Type-4 travel four bytes late, so only the boots slot
    // (ARMOR[4]) reports a trustworthy durability. HuxleyQuestGame.ComputePlayerBattleResult takes
    // the same DurabilityDecParam + penalty off every armor piece and every weapon in WeaponStats
    // (HuxleyMultiWeapon.GivenTo adds each equipped weapon at spawn), so the boots delta applies
    // to all equipped armor and weapons.
    public int UpdateQuestDurabilityFromBoots(int pid, byte reportedBootsDurability)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return 0;
            var boots = ItemAt(list, InventoryType.Armor, 4);
            if (boots == null || reportedBootsDurability > boots.Durability) return 0;
            // Zero from healthy boots means the run never loaded the gear, not wear: no run takes half
            // of a boot's durability, and this would break all equipment at once.
            if (reportedBootsDurability == 0 && boots.Durability > MaxDurability / 2) return 0;
            int wear = boots.Durability - reportedBootsDurability;
            if (wear == 0) return 0;
            int changed = 0;
            foreach (var item in list.Where(i => i.InventoryIndex is InventoryType.Weapon or InventoryType.Armor))
            {
                int next = Math.Max(0, item.Durability - wear);
                if (next == item.Durability) continue;
                item.Durability = next;
                changed++;
            }
            if (changed != 0) SaveInventory();
            return changed;
        }
    }

    // Charged in QT and committed atomically with the deduction.
    public bool TryRepair(int pid, byte? inventory, byte? cell, bool equippedOnly, int discountPercent,
        out List<GenericItem> repaired, out long qt)
    {
        repaired = [];
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            qt = progress.QT;
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            IEnumerable<GenericItem> candidates = list.Where(i =>
                inventory.HasValue ? i.InventoryIndex == inventory && (!cell.HasValue || i.InventoryCell == cell) :
                equippedOnly ? i.InventoryIndex is >= 1 and <= InventoryType.Armor :
                i.InventoryIndex is >= InventoryType.Main and <= InventoryType.Armor);
            // HuxleyItemRepairMenu lists only EILT_None items: time-limited gear is never repaired.
            var targets = candidates.Where(i => i.Durability < MaxDurability)
                .Select(i => (item: i, data: _itemCatalog.Get(i.ItemId)))
                .Where(x => x.data?.Repairable == true && x.data.ItemLimitTimeType == 0).ToList();
            if (targets.Count == 0) return false;

            long cost = targets.Sum(t => RepairCost(t.item, t.data!));
            // ECTLG_Repair: RepairTalent discount is a (1.0 - DiscountAmount*0.01) multiplier.
            int repairDiscount = Math.Clamp(discountPercent, 0, 100);
            float repairRate = _economyRates.ForPid(pid).Repair;
            cost = checked((long)(cost * (1.0f - repairDiscount * 0.01f) * repairRate));
            if (cost < 0 || progress.QT < cost) return false;
            progress.QT -= cost;
            foreach (var target in targets)
            {
                target.item.Durability = MaxDurability;
                repaired.Add(Clone(target.item));
            }
            qt = progress.QT;
            SaveInventory(); SaveProgress();
            return true;
        }
    }

    // (100 - durability) * RepairQt[level class][grade]. HuxleyItemRepairMenu.GetRepairsCost reads
    // ItemGradeParam[ItemGrade - 1] from a six-entry array, so Relic falls past its end and the
    // client shows and checks a cost of 0.
    private long RepairCost(GenericItem item, ItemCatalog.Item data)
    {
        if (data.ItemGrade - 1 > 5) return 0;
        int gradeIndex = Math.Max(data.ItemGrade - 1, 0);
        return (long)(MaxDurability - Math.Clamp(item.Durability, 0, MaxDurability))
            * RepairQt[GetLevelClass(data.EquipLevel)][gradeIndex];
    }

    // HuxleyMultiGame.GetLevelClass: class boundaries at levels 10, 20, and 30.
    private static int GetLevelClass(int level) => level switch
    {
        >= 30 => 3,
        >= 20 => 2,
        >= 10 => 1,
        _ => 0
    };
}
