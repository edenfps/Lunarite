using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AuctionRegisterCancelHandler
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
        if (!reader.TryRead(out int index) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        AuctionStore.Result result = AuctionStore.Result.Error;
        PlayerStore.GenericItem? returned = null;
        _multiStoreTransaction.Run(() =>
        {
            result = _auctionStore.Cancel(pid, index, out returned);
            if (result == AuctionStore.Result.Success && returned != null)
                _itemManager.SynchronizeQuestPickupObjective(pid, returned.ItemId);
        });
        client.SendPacket(new AuctionResult { Opcode = 165, Answer = (byte)result });
        if (returned != null) ItemManager.SendInventoryItem(client, returned);
        return true;
    }
}
