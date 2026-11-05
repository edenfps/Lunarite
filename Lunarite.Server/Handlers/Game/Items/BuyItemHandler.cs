using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BuyItemHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemCatalog _itemCatalog = null!;
    private static ItemGroupCatalog _itemGroupCatalog = null!;
    private static ItemManager _itemManager = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _itemCatalog = serviceProvider.GetRequiredService<ItemCatalog>();
        _itemGroupCatalog = serviceProvider.GetRequiredService<ItemGroupCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int itemId) || !reader.TryRead(out byte cell)
            || !reader.TryRead(out int count) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var before = _playerStore.GetInventory(pid, () => []).Where(i => i.InventoryIndex == InventoryType.Main)
            .ToDictionary(i => i.InventoryCell);
        var data = _itemCatalog.Get(itemId);
        PlayerStore.GenericItem? item = null;
        long money = _playerStore.GetProgress(pid).Money;
        byte result = data == null || !_itemGroupCatalog.IsVendorItem(itemId) ? (byte)7 : (byte)1;
        bool bought = false;
        if (data != null && result != 7)
            _multiStoreTransaction.Run(() =>
            {
                bought = _playerStore.TryBuyItem(pid, data, cell, count, out item, out money, out result);
                if (bought) _itemManager.SynchronizeQuestPickupObjective(pid, itemId);
            });
        if (bought && item != null)
        {
            var after = _playerStore.GetInventory(pid, () => []).Where(i => i.InventoryIndex == InventoryType.Main)
                .ToDictionary(i => i.InventoryCell);
            foreach (byte changedCell in before.Keys.Union(after.Keys).Order())
            {
                before.TryGetValue(changedCell, out var oldItem);
                after.TryGetValue(changedCell, out var newItem);
                if (oldItem?.ItemId == newItem?.ItemId && oldItem?.Count == newItem?.Count) continue;
                if (newItem == null)
                    client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = changedCell }); // see DropItemHandler
                else if (oldItem?.ItemId == newItem.ItemId)
                    client.SendPacket(new UpdateItemNum
                    {
                        Inventory = InventoryType.Main, Cell = changedCell, Count = newItem.Count
                    });
                else
                {
                    client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = changedCell });
                    ItemManager.SendInventoryItem(client, newItem);
                }
            }
            if (data!.OnlyQtItem)
                client.SendPacket(new Parameter { Param = Parameter.PARAMETER_QT,
                    Value = (int)Math.Min(int.MaxValue, _playerStore.GetProgress(pid).QT) });
            else
                client.SendPacket(new Parameter { Param = Parameter.PARAMETER_MONEY,
                    Value = (int)Math.Min(int.MaxValue, money) });
        }
        client.SendPacket(new BuyItem { Result = result });
        return true;
    }
}
