using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class JoinChannelHandler
{
    private static SimulationManager _simulationManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(JoinChannelHandler));
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // The June native sender accepts group + channel but transmits only channel.
        if (!reader.TryRead(out byte channelId) || reader.RemainingLength != 0)
            return false;

        if (channelId != 0)
        {
            client.SendPacket(new JoinChannel { Result = SimulationAnswer.Error });
            return true;
        }

        client.SendPacket(new JoinChannel());
        int pid = Game.PidOf(client);
        _simulationManager.JoinChannel(client, pid);
        _logger.LogInformation("[SimulationCenter] Joined channel {0}; sent {1} room(s).", channelId, _simulationManager.RoomCount);
        return true;
    }
}
