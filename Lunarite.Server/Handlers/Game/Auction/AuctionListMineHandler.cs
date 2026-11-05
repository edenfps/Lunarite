using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AuctionListMineHandler
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
        _auctionManager.SendAuctionPage(client, 162, _auctionStore.Active().Where(x => x.SellerPid == pid), page);
        return true;
    }
}
