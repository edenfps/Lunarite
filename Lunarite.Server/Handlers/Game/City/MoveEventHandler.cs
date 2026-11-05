using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class MoveEventHandler
{
    private static CityManager _cityManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MoveEventHandler));
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!MoveEventRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(MoveEventRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(MoveEventRequest), packet);

        int pid = Game.PidOf(client);
        if (pid > 0 && Game.PlayerFor(pid).IsSpawned)
            _cityManager.BroadcastPresence(client, new MoveEvent { Pid = pid, Event = packet.Event });

        return true;
    }
}
