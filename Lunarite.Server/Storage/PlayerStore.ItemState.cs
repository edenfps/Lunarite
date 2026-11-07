using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

// Item ownership state: binding bits, timed ownership, and newly acquired records.
public sealed partial class PlayerStore
{
    public sealed record ExpiredItem(byte Inventory, byte Cell, int ItemId, int TuneUpOptions, byte Type);

    // Shop, loot, and reward items get cooked binding, durability, and possessed-time rules once,
    // at acquisition. Transfers keep their existing GenericItem instead.
    public GenericItem CreateAcquiredItem(int itemId, int count = 1, int? unixSeconds = null)
    {
        var data = _itemCatalog.Get(itemId);
        var item = new GenericItem
        {
            ItemId = itemId,
            Count = count,
            Durability = data?.Repairable == true ? MaxDurability : 0
        };
        ApplyPickupBinding(item, data);
        StartPossessedLimit(item, data, unixSeconds ?? UnixNow());
        return item;
    }

    // EILT_Equipped (2): the minute duration starts on first equip and persists as absolute Unix time.
    public bool TryStartEquippedLimit(int pid, byte inventory, byte cell, out int limitTime)
    {
        limitTime = 0;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var item = ItemAt(list, inventory, cell);
            if (item == null) return false;
            var data = _itemCatalog.Get(item.ItemId);
            if (data is not { ItemLimitTimeType: 2, ItemLimitDuration: > 0 } || item.LimitTime != 0)
                return false;
            item.LimitTime = checked((int)DateTimeOffset.UtcNow.AddMinutes(data.ItemLimitDuration).ToUnixTimeSeconds());
            limitTime = item.LimitTime;
            SaveInventory();
            return true;
        }
    }

    public bool TryBindEquipped(int pid, byte inventory, byte cell, out byte state)
    {
        state = 0;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var item = ItemAt(list, inventory, cell);
            if (item == null || _itemCatalog.Get(item.ItemId)?.BindOnEquip != true || (item.State & ItemStateBind) != 0)
                return false;
            item.State |= ItemStateBind;
            state = item.State;
            SaveInventory();
            return true;
        }
    }

    public List<ExpiredItem> PruneExpiredInventory(int pid, int? unixSeconds = null)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return [];
            int now = unixSeconds ?? UnixNow();
            bool IsExpired(GenericItem i) => i.LimitTime > 0 && i.LimitTime <= now
                && (_itemCatalog.Get(i.ItemId)?.ArmorPart.Length ?? 0) == 0;
            var expiredSlots = list.Where(IsExpired).ToList();
            var expired = expiredSlots
                .Select(i => new ExpiredItem(i.InventoryIndex, i.InventoryCell, i.ItemId, i.TuneUpOptions, 0)).ToList();
            if (expired.Count == 0) return expired;
            list.RemoveAll(i => IsExpired(i));
            if (_itemTotals.TryGetValue(pid, out var totals))
            {
                foreach (var group in expiredSlots.GroupBy(x => x.ItemId))
                {
                    int next = totals.GetValueOrDefault(group.Key) - group.Sum(x => x.Count);
                    if (next <= 0) totals.Remove(group.Key); else totals[group.Key] = next;
                }
            }
            SaveItemTotals(); SaveInventory();
            return expired;
        }
    }

    // Clears ITEMSTATE_BIND and counts the reset in the high nibble, up to MAX_RESETBIND_COUNT.
    public bool TryRemoveItemBind(int pid, byte useCell, byte targetCell, out GenericItem? targetAfter)
    {
        targetAfter = null;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var use = ItemAt(list, InventoryType.Main, useCell);
            var target = ItemAt(list, InventoryType.Main, targetCell);
            if (use == null || target == null || use == target || _itemCatalog.Get(use.ItemId)?.RemoveItemBind != true
                || (target.State & ItemStateBindTag) != ItemStateBind) return false;
            int resetCount = (target.State & ItemStateBindCountTag) >> 4;
            if (resetCount >= MaxResetBindCount) return false;
            if (--use.Count <= 0) list.Remove(use);
            ReduceTotal(pid, use.ItemId, 1);
            target.State = (byte)((resetCount + 1) << 4);
            targetAfter = Clone(target);
            SaveItemTotals(); SaveInventory();
            return true;
        }
    }

    private static void StartPossessedLimit(GenericItem item, ItemCatalog.Item? data, int now)
    {
        if (item.LimitTime != 0 || data == null || data.ArmorPart.Length != 0) return;
        if (data is { ItemLimitTimeType: 1, ItemLimitDuration: > 0 })
            item.LimitTime = checked(now + data.ItemLimitDuration * 60);
        else if (data is { ItemLimitTimeType: 3, FixedLimitTime: > 0 })
            item.LimitTime = data.FixedLimitTime;
    }

    // ITEMSTATE_BIND_WHEN_PICKUP applies when ownership is created, unlike bind-on-equip.
    private void ApplyPickupBinding(GenericItem item, ItemCatalog.Item? data)
    {
        if (data?.BindOnPickup == true) item.State |= ItemStateBindWhenPickup;
    }

    // An empty MAIN stack for a newly acquired item, before its count is added.
    private GenericItem NewMainStack(byte cell, int itemId, ItemCatalog.Item? data, int count = 0) => new()
    {
        InventoryIndex = InventoryType.Main,
        InventoryCell = cell,
        ItemId = itemId,
        Count = count,
        Durability = data?.Repairable == true ? MaxDurability : 0
    };

    private static int UnixNow() => checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
}
