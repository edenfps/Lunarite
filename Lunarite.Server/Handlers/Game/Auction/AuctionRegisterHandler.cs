using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AuctionRegisterHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static CharacterManager _characterManager = null!;
    private static ItemManager _itemManager = null!;
    private static AuctionStore _auctionStore = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _auctionStore = serviceProvider.GetRequiredService<AuctionStore>();
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!AuctionRegisterRequest.TryDeserialize(ref reader, out var packet)) return false;
        int pid = Game.PidOf(client);
        AuctionStore.Result result = AuctionStore.Result.Error;
        AuctionStore.AuctionItemInfo? listing = null;
        _multiStoreTransaction.Run(() =>
        {
            result = _auctionStore.Register(pid, _characterStore.Get(pid)?.Name ?? "",
                packet.MainCell, packet.SellPrice, packet.BidPeriod, out listing);
            if (result == AuctionStore.Result.Success && listing != null)
                _itemManager.SynchronizeQuestPickupObjective(pid, listing.Item.ItemId);
        });
        client.SendPacket(new AuctionResult { Opcode = 164, Answer = (byte)result,
            Cell = result == AuctionStore.Result.Success ? packet.MainCell : null });
        if (result == AuctionStore.Result.Success)
            _characterManager.SendMoneyParameter(client, pid);
        return true;
    }
}
