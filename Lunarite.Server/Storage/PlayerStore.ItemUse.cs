using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    private const int CityMedalA = 609001;
    private const int CityMedalB = 609101;

    public bool TryConsumeMainItem(int pid, byte cell, int expectedItemId, out GenericItem? remaining)
    {
        remaining = null;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var item = ItemAt(list, InventoryType.Main, cell);
            if (item == null || item.ItemId != expectedItemId || item.Count < 1) return false;
            remaining = ConsumeOne(pid, list, item);
            SaveItemTotals(); SaveInventory();
            return true;
        }
    }

    // GeneralValue 10 marks an inventory/bank expansion item; each use opens one more step.
    public bool TryUseExpansionItem(int pid, byte mainCell, bool bank, int generalValue,
        out byte newStep)
    {
        newStep = 0;
        if (generalValue != 10) return false;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var item = ItemAt(list, InventoryType.Main, mainCell);
            if (item == null) return false;
            var progress = ProgressFor(pid);
            byte step = bank ? progress.BankExpandStep : progress.InvenExpandStep;
            byte maximum = bank ? (byte)4 : (byte)2;
            if (step >= maximum) return false;
            if (--item.Count <= 0) list.Remove(item);
            newStep = ++step;
            if (bank) progress.BankExpandStep = step; else progress.InvenExpandStep = step;
            ReduceTotal(pid, item.ItemId, 1);
            SaveItemTotals(); SaveInventory(); SaveProgress();
            return true;
        }
    }

    // The upper-body armor keeps the spray, which HuxleyMultiGame resolves through ItemArmor.nSpray to
    // HuxleyCityItemData_ItemColorSpray.ItemColor. See GenericItem.WireDurability.
    public bool TryUseColorSpray(int pid, byte sprayCell, int sprayItemId,
        out GenericItem? remainingSpray, out GenericItem? upbody)
    {
        remainingSpray = null; upbody = null;
        // The client can only name a spray in the 16 bits above SprayItemBase; 9011xxxx rows cannot be shown.
        if (sprayItemId <= SprayItemBase || sprayItemId > SprayItemBase + 0xFFFF) return false;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var spray = ItemAt(list, InventoryType.Main, sprayCell);
            var armor = ItemAt(list, InventoryType.Armor, 1); // Pos_Upbody
            if (spray == null || spray.ItemId != sprayItemId || spray.Count < 1 || armor == null) return false;

            armor.Spray = sprayItemId - SprayItemBase;
            remainingSpray = ConsumeOne(pid, list, spray);
            upbody = Clone(armor);
            SaveItemTotals(); SaveInventory();
            return true;
        }
    }

    // ECIG_HPointCoupon: GeneralValue is the exact QT grant.
    public bool TryUseHPointCoupon(int pid, byte cell, int itemId, int qtGrant,
        out GenericItem? remaining, out long qt)
    {
        remaining = null; qt = 0;
        if (qtGrant <= 0) return false;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var item = ItemAt(list, InventoryType.Main, cell);
            if (item == null || item.ItemId != itemId || item.Count < 1) return false;
            var progress = ProgressFor(pid);
            if (progress.QT > long.MaxValue - qtGrant) return false;

            progress.QT += qtGrant;
            qt = progress.QT;
            remaining = ConsumeOne(pid, list, item);
            SaveItemTotals(); SaveInventory(); SaveProgress();
            return true;
        }
    }

    public bool TryDonateCityMedals(int pid, int aId, int aCount, int bId, int bCount)
    {
        if (aId != CityMedalA || bId != CityMedalB || aCount < 0 || bCount < 0 || (aCount == 0 && bCount == 0))
            return false;
        lock (_lock)
        {
            // Rank is only judged at RankTable.RequiredLevel (TryPromoteRank) and nothing re-judges
            // it on level-up, so an earlier donation would be consumed without ever promoting.
            // Refuse it (MedalParameterUpdate failure) and leave the medals in the inventory.
            if (LevelForBP(ProgressFor(pid).BP) < RankTable.RequiredLevel) return false;
            if (!_inventory.TryGetValue(pid, out var slots)) return false;
            if (MainCount(slots, CityMedalA) < aCount || MainCount(slots, CityMedalB) < bCount)
                return false;

            ConsumeMedals(slots, CityMedalA, aCount);
            ConsumeMedals(slots, CityMedalB, bCount);
            if (aCount > 0) ReduceTotal(pid, CityMedalA, aCount);
            if (bCount > 0) ReduceTotal(pid, CityMedalB, bCount);
            var progress = ProgressFor(pid);
            progress.AMedalsDonated = checked(progress.AMedalsDonated + aCount);
            progress.BMedalsDonated = checked(progress.BMedalsDonated + bCount);
            SaveInventory(); SaveItemTotals(); SaveProgress();
            return true;
        }
    }

    private static void ConsumeMedals(List<GenericItem> list, int itemId, int count)
    {
        foreach (var slot in list.Where(x => x.InventoryIndex == InventoryType.Main && x.ItemId == itemId).ToList())
        {
            int take = Math.Min(count, slot.Count);
            slot.Count -= take;
            count -= take;
            if (slot.Count == 0) list.Remove(slot);
            if (count == 0) break;
        }
    }

    private static int MainCount(List<GenericItem> list, int itemId) =>
        list.Where(x => x.InventoryIndex == InventoryType.Main && x.ItemId == itemId).Sum(x => x.Count);

    // Uses one unit from a MAIN stack and returns what is left of it, or null once it empties.
    private GenericItem? ConsumeOne(int pid, List<GenericItem> list, GenericItem item)
    {
        item.Count--;
        ReduceTotal(pid, item.ItemId, 1);
        if (item.Count != 0) return Clone(item);
        list.Remove(item);
        return null;
    }
}
