using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class DropItemHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte inven) || !reader.TryRead(out byte cell)
            || !reader.TryRead(out int count) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        int itemId = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(item => item.InventoryIndex == inven && item.InventoryCell == cell)?.ItemId ?? 0;
        bool dropped = false;
        PlayerStore.GenericItem? remaining = null;
        // The client drops only from MAIN and BANK; equipped and socketed items stay put.
        bool droppable = inven == InventoryType.Main || inven == InventoryType.Bank;
        _multiStoreTransaction.Run(() =>
        {
            dropped = droppable && _playerStore.TryDropItem(pid, inven, cell, count, out remaining);
            if (dropped && inven == InventoryType.Main && itemId > 0)
                _itemManager.SynchronizeQuestPickupObjective(pid, itemId);
        });
        if (dropped)
        {
            // RemoveItem, not UpdateItemIndex(0): the client's UpdateItemIndex only overwrites the cell's item id and
            // leaves the inventory's item count up, so every cleared cell made the client think it was fuller, until
            // OnInsertItem refused buys and rewards as full (they appeared after a relog; live 2026-11-05).
            if (remaining != null)
                client.SendPacket(new UpdateItemNum { Inventory = inven, Cell = cell, Count = remaining.Count });
            else
                client.SendPacket(new RemoveItem { InventoryIndex = inven, Cell = cell });
        }
        client.SendPacket(new DropItem { Result = dropped ? (byte)0 : (byte)1 });
        return true;
    }
}
