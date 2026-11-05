using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AuctionCheckTimeOutHandler
{
    private static AuctionStore _auctionStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _auctionStore = serviceProvider.GetRequiredService<AuctionStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        client.SendPacket(new AuctionCheckTimeout { HasItems = _auctionStore.HasTimedOut(pid) });
        return true;
    }
}
