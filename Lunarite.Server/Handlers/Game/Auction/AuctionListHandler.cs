using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AuctionListHandler
{
    private static AuctionManager _auctionManager = null!;
    private static AuctionStore _auctionStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _auctionStore = serviceProvider.GetRequiredService<AuctionStore>();
        _auctionManager = serviceProvider.GetRequiredService<AuctionManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native sender wire format: category/page/usable/grade/search = `bbbbs`.
        if (!AuctionListRequest.TryDeserialize(ref reader, out var packet)) return false;
        int pid = Game.PidOf(client);
        IEnumerable<AuctionStore.AuctionItemInfo> listings = _auctionStore.Search(pid, packet.Category,
            packet.Usable, packet.Grade, packet.Search);
        _auctionManager.SendAuctionPage(client, 161, listings, packet.Page);
        return true;
    }
}
