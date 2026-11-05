using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AuctionReturnAllHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static AuctionStore _auctionStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _auctionStore = serviceProvider.GetRequiredService<AuctionStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        List<AuctionStore.ReturnedItem> returnedItems = [];
        _multiStoreTransaction.Run(() =>
        {
            returnedItems = _auctionStore.ReturnTimedOutDetailed(pid);
            _itemManager.SynchronizeQuestPickupObjectives(pid, returnedItems.Select(item => item.ItemId));
        });
        foreach (var returned in returnedItems)
        {
            ItemManager.SendInventoryItem(client, returned.Item);
            client.SendPacket(new AuctionNotification
            {
                Opcode = 168, Result = (byte)AuctionStore.Result.Success, ItemId = returned.ItemId
            });
        }
        return true;
    }
}
