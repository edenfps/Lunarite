using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    public enum TradeCommitResult : byte
    {
        Success,
        Invalid,
        AInventoryFull,
        BInventoryFull
    }

    // HuxleyUserTradeMenu.INVENSIZE_TRADE, indexed 0..6.
    public const byte TradeSlotCapacity = 7;

    // HuxleyUserTradeMenu checks this before Transaction_Trade_AddItem; the server repeats it
    // because the request only names client-selected cells. Native IsRepairable(GenericItem) is a
    // damaged-item predicate: fully repaired equipment may be traded, damaged equipment may not.
    public bool CanPlayerTradeItem(GenericItem item)
    {
        var data = _itemCatalog.Get(item.ItemId);
        return (item.State & ItemStateBindTag) == 0
            && data != null
            && !(data.Repairable && item.Durability < MaxDurability)
            && data.ItemSalesCategory != 1; // HuxleyInventoryData.CanTradeItem: EISC_PCRoom=false
    }

    public bool TryCompleteTrade(int aPid, IReadOnlyDictionary<byte, byte> aOffers, int aMoney,
        int bPid, IReadOnlyDictionary<byte, byte> bOffers, int bMoney) =>
        CompleteTrade(aPid, aOffers, aMoney, bPid, bOffers, bMoney) == TradeCommitResult.Success;

    // Commits both sides atomically. Offers map trade slot -> MAIN cell; nothing changes unless both
    // balances, every source cell, and both destination capacities validate at confirmation. The
    // optional snapshots (trade slot -> item shown to the partner) must still match their cells, so
    // a swap, split or rearrange after the partner confirmed cannot hand over a different item.
    public TradeCommitResult CompleteTrade(int aPid, IReadOnlyDictionary<byte, byte> aOffers, int aMoney,
        int bPid, IReadOnlyDictionary<byte, byte> bOffers, int bMoney,
        IReadOnlyDictionary<byte, GenericItem>? aOffered = null, IReadOnlyDictionary<byte, GenericItem>? bOffered = null)
    {
        if (aPid == bPid || aMoney < 0 || bMoney < 0) return TradeCommitResult.Invalid;
        if (aOffers.Keys.Any(slot => slot >= TradeSlotCapacity)
            || bOffers.Keys.Any(slot => slot >= TradeSlotCapacity)) return TradeCommitResult.Invalid;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(aPid, out var aCurrent) || !_inventory.TryGetValue(bPid, out var bCurrent))
                return TradeCommitResult.Invalid;
            var aProg = ProgressFor(aPid);
            var bProg = ProgressFor(bPid);
            if (aProg.Money < aMoney || bProg.Money < bMoney) return TradeCommitResult.Invalid;
            // nMoney is an int on the client: refuse rather than push a balance past MaxMoney.
            if (aProg.Money - aMoney + bMoney > MaxMoney || bProg.Money - bMoney + aMoney > MaxMoney)
                return TradeCommitResult.Invalid;
            if (aOffers.Values.Distinct().Count() != aOffers.Count || bOffers.Values.Distinct().Count() != bOffers.Count)
                return TradeCommitResult.Invalid;

            var aWork = aCurrent.Select(Clone).ToList();
            var bWork = bCurrent.Select(Clone).ToList();
            var fromA = TakeTradeItems(aWork, aOffers.OrderBy(x => x.Key), aOffered);
            var fromB = TakeTradeItems(bWork, bOffers.OrderBy(x => x.Key), bOffered);
            if (fromA == null || fromB == null) return TradeCommitResult.Invalid;
            if (!DepositTradeItems(aWork, fromB, MainCapacity(aPid))) return TradeCommitResult.AInventoryFull;
            if (!DepositTradeItems(bWork, fromA, MainCapacity(bPid))) return TradeCommitResult.BInventoryFull;

            _inventory[aPid] = aWork;
            _inventory[bPid] = bWork;
            aProg.Money = checked(aProg.Money - aMoney + bMoney);
            bProg.Money = checked(bProg.Money - bMoney + aMoney);
            _itemTotals[aPid] = TotalsOf(aWork);
            _itemTotals[bPid] = TotalsOf(bWork);
            SaveInventory(); SaveProgress(); SaveItemTotals();
            return TradeCommitResult.Success;
        }
    }

    private List<GenericItem>? TakeTradeItems(List<GenericItem> source,
        IEnumerable<KeyValuePair<byte, byte>> offers, IReadOnlyDictionary<byte, GenericItem>? offered)
    {
        var result = new List<GenericItem>();
        foreach (var (slot, cell) in offers)
        {
            var item = ItemAt(source, InventoryType.Main, cell);
            if (item == null || !CanPlayerTradeItem(item)) return null;
            if (offered != null && (!offered.TryGetValue(slot, out var shown) || !IsSameTradeItem(item, shown)))
                return null;
            source.Remove(item);
            result.Add(item);
        }
        return result;
    }

    // Every GenericItem field the partner saw in the op85 AddItem snapshot.
    private static bool IsSameTradeItem(GenericItem current, GenericItem shown) =>
        current.ItemId == shown.ItemId && current.Count == shown.Count && current.State == shown.State
        && current.LimitTime == shown.LimitTime && current.TuneUpOptions == shown.TuneUpOptions
        && current.Durability == shown.Durability && current.Skill == shown.Skill && current.Skill1 == shown.Skill1;

    private static bool DepositTradeItems(List<GenericItem> target, IEnumerable<GenericItem> items, int capacity)
    {
        var used = target.Where(x => x.InventoryIndex == InventoryType.Main).Select(x => x.InventoryCell).ToHashSet();
        foreach (var item in items)
        {
            int free = Enumerable.Range(0, capacity).FirstOrDefault(x => !used.Contains((byte)x), -1);
            if (free < 0) return false;
            item.InventoryIndex = InventoryType.Main;
            item.InventoryCell = (byte)free;
            used.Add((byte)free);
            target.Add(item);
        }
        return true;
    }

    private static Dictionary<int, int> TotalsOf(List<GenericItem> list) => list
        .Where(x => x.Count > 0).GroupBy(x => x.ItemId).ToDictionary(x => x.Key, x => x.Sum(y => y.Count));
}
