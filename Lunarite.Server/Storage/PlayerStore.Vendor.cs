using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    // Native RESULTTYPE_ITEM_* codes returned by op73.
    private const byte BuyOk = 0;
    private const byte BuyError = 1;
    private const byte BuyInventoryFull = 2;
    private const byte BuyInvalidCell = 6;
    private const byte BuyInvalidItemId = 7;
    private const byte BuyCellOccupied = 9;
    private const byte BuyNotEnoughMoney = 14;
    private const byte BuyNotEnoughMedal = 33;
    private const byte BuyPcRoomItem = 48;

    public bool TryBuyItem(int pid, ItemCatalog.Item data, byte requestedCell, int count,
        out GenericItem? purchased, out long money, out byte result)
    {
        purchased = null;
        lock (_lock)
        {
            bool hasProgress = _progress.TryGetValue(pid, out var stored);
            var progress = stored ?? new PlayerProgress();
            money = progress.Money;
            bool hasInventory = _inventory.TryGetValue(pid, out var list);
            list ??= [];

            result = CheckPurchase(pid, data, count, progress, list, out long cost);
            if (result != BuyOk) return false;
            result = ResolvePurchaseSlot(pid, list, data, requestedCell, count, out var slot);
            if (result != BuyOk) return false;

            ConsumeExchangeItems(pid, list, data, count);
            slot!.Count += count;
            ApplyPickupBinding(slot, data);
            StartPossessedLimit(slot, data, UnixNow());
            if (!hasInventory) _inventory[pid] = list;
            if (!hasProgress) _progress[pid] = progress;
            if (data.OnlyQtItem)
                progress.QT = checked(progress.QT - cost + (long)data.AdditionalQt * count);
            else
                progress.Money -= cost;
            AddTotal(pid, data.EntryIndex, count);
            purchased = Clone(slot);
            money = progress.Money;
            SaveItemTotals(); SaveInventory(); SaveProgress();
            return true;
        }
    }

    // Repeats the vendor menu's client-side gates, since op73 can name any cooked item.
    private byte CheckPurchase(int pid, ItemCatalog.Item data, int count, PlayerProgress progress,
        List<GenericItem> list, out long cost)
    {
        cost = 0;
        // Both exported vendor menus hide rows with !bEnabled.
        if (!data.Enabled || data.OnlyForSuperUser) return BuyInvalidItemId;
        // HuxleyTradeMenu gates op73 on CanGetItem(PRI.IsPCRoomValid()); sessions count as PC-room
        // only while the operator's PC-room event is on.
        if (_serverEvents.BlocksPcRoomItem(data)) return BuyPcRoomItem;
        if (data is { ItemLimitTimeType: 3, FixedLimitTime: > 0 }
            && data.FixedLimitTime <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return BuyInvalidItemId;
        bool unpriced = data.OnlyQtItem ? data.QtPrice <= 0 : data.Price <= 0 && data.ExchangeItems.Count == 0;
        if (count <= 0 || unpriced || count > data.MaxCollapseCount) return BuyError;

        float buyRate = _economyRates.ForPid(pid).Buy;
        long unitCost = data.OnlyQtItem ? data.QtPrice : checked((int)(data.Price * buyRate));
        cost = checked(unitCost * count);
        long balance = data.OnlyQtItem ? progress.QT : progress.Money;
        if (cost > balance || cost > int.MaxValue) return BuyNotEnoughMoney;

        foreach (var exchange in data.ExchangeItems)
        {
            long required = checked((long)exchange.Count * count);
            long owned = list.Where(i => i.InventoryIndex == InventoryType.Main && i.ItemId == exchange.ItemId)
                .Sum(i => (long)i.Count);
            if (owned < required) return BuyNotEnoughMedal;
        }
        return BuyOk;
    }

    // Cell 0xFF tops up a compatible stack or takes the first free cell. A new empty stack is
    // added to the list here; the caller fills in its count.
    private byte ResolvePurchaseSlot(int pid, List<GenericItem> list, ItemCatalog.Item data,
        byte requestedCell, int count, out GenericItem? slot)
    {
        if (requestedCell == 0xFF)
        {
            slot = list.FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.ItemId == data.EntryIndex
                && i.Count + count <= data.MaxCollapseCount);
            if (slot != null) return BuyOk;
            int free = FindEmptyCell(list, MainCapacity(pid));
            if (free < 0) return BuyInventoryFull;
            slot = NewMainStack((byte)free, data.EntryIndex, data);
            list.Add(slot);
            return BuyOk;
        }

        slot = null;
        if (requestedCell >= MainCapacity(pid)) return BuyInvalidCell;
        slot = ItemAt(list, InventoryType.Main, requestedCell);
        if (slot == null)
        {
            slot = NewMainStack(requestedCell, data.EntryIndex, data);
            list.Add(slot);
            return BuyOk;
        }
        return slot.ItemId != data.EntryIndex || slot.Count + count > data.MaxCollapseCount
            ? BuyCellOccupied : BuyOk;
    }

    private void ConsumeExchangeItems(int pid, List<GenericItem> list, ItemCatalog.Item data, int count)
    {
        foreach (var exchange in data.ExchangeItems)
        {
            int remaining = checked(exchange.Count * count);
            foreach (var material in list
                .Where(i => i.InventoryIndex == InventoryType.Main && i.ItemId == exchange.ItemId).ToList())
            {
                int take = Math.Min(remaining, material.Count);
                material.Count -= take;
                remaining -= take;
                if (material.Count == 0) list.Remove(material);
                if (remaining == 0) break;
            }
            if (_itemTotals.TryGetValue(pid, out var totals))
            {
                int next = Math.Max(0, totals.GetValueOrDefault(exchange.ItemId) - exchange.Count * count);
                if (next == 0) totals.Remove(exchange.ItemId); else totals[exchange.ItemId] = next;
            }
        }
    }

    public bool TrySellMainItem(int pid, byte cell, int count, ItemCatalog.Item data,
        out GenericItem? remaining, out long money) =>
        TrySellMainItem(pid, cell, count, data, out remaining, out money, out _, out _);

    // ENUM_RESULTTYPE_ITEM codes the sell callback (HuxleyPCPlayerInventoryMenu) tells apart.
    public const byte SellNotExist = ItemResultCode.Notexist, SellWrongParameter = ItemResultCode.WrongParameter,
        SellCannotSell = ItemResultCode.Cannotsell, SellMaxMoney = ItemResultCode.Maxmoney;

    // HuxleyPlayerWrapper.nMoney is an int, so a sale that would pass int.MaxValue is refused
    // with RESULTTYPE_ITEM_MAXMONEY.
    public bool TrySellMainItem(int pid, byte cell, int count, ItemCatalog.Item data,
        out GenericItem? remaining, out long money, out long proceeds, out byte result)
    {
        remaining = null;
        proceeds = 0;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            money = progress.Money;
            List<GenericItem> list = _inventory.TryGetValue(pid, out var owned) ? owned : [];
            var item = ItemAt(list, InventoryType.Main, cell);
            result = item == null || item.ItemId != data.EntryIndex ? SellNotExist
                : count <= 0 || count > item.Count ? SellWrongParameter
                : !data.PermitSell ? SellCannotSell : (byte)0;
            if (item == null || result != 0) return false;

            // HuxleyInventoryDataDescriptor scales armor and weapon data (the repairable classes)
            // by durability.
            float sellRate = _economyRates.ForPid(pid).Sell;
            long unitPrice = data.Repairable
                ? checked((int)(data.SellPrice * sellRate * (Math.Clamp(item.Durability, 0, MaxDurability) / 100.0f)))
                : checked((int)(data.SellPrice * sellRate));
            proceeds = unitPrice * count;
            if (progress.Money + proceeds > MaxMoney) { proceeds = 0; result = SellMaxMoney; return false; }
            item.Count -= count;
            if (item.Count == 0) list.Remove(item); else remaining = Clone(item);
            ReduceTotal(pid, item.ItemId, count);
            progress.Money += proceeds;
            money = progress.Money;
            SaveItemTotals(); SaveInventory(); SaveProgress();
            return true;
        }
    }

    // Reverses one vendor sale: charges back its proceeds and restores the sold GenericItem into
    // the first free MAIN cell.
    public bool TryUndoSale(int pid, GenericItem source, long price, out GenericItem deposited, out long money)
    {
        deposited = new();
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            money = progress.Money;
            if (price < 0 || progress.Money < price) return false;
            if (!_inventory.TryGetValue(pid, out var list)) list = _inventory[pid] = [];
            int free = FindEmptyCell(list, MainCapacity(pid));
            if (free < 0) return false;

            deposited = Clone(source);
            deposited.InventoryIndex = InventoryType.Main;
            deposited.InventoryCell = (byte)free;
            list.Add(deposited);
            AddTotal(pid, deposited.ItemId, deposited.Count);
            progress.Money -= price;
            money = progress.Money;
            SaveItemTotals(); SaveInventory(); SaveProgress();
            return true;
        }
    }
}
