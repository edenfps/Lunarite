using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TradeHandler
{
    private static TradeManager _tradeManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _tradeManager = serviceProvider.GetRequiredService<TradeManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte action)) return false;
        int pid = Game.PidOf(client);
        if (pid == 0) return false;
        int argument = 0;
        byte first = 0, second = 0;
        bool valid = action switch
        {
            0 or 1 or 2 or 7 or 12 => reader.TryRead(out argument),
            5 => reader.TryRead(out first) && reader.TryRead(out second),
            6 => reader.TryRead(out first),
            8 or 10 => true,
            _ => false
        };
        if (!valid || reader.RemainingLength != 0) return false;
        _tradeManager.Trade(client, pid, action, argument, first, second);
        return true;
    }
}
