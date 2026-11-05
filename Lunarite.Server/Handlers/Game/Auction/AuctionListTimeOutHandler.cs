using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AuctionListTimeOutHandler
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
        if (!reader.TryRead(out int page) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        _auctionManager.SendAuctionPage(client, 167, _auctionStore.TimedOut(pid), page);
        return true;
    }
}
