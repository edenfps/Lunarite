using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SCChannelListHandler
{
    private static SimulationManager _simulationManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(SCChannelListHandler));
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0)
            return false;

        var response = new SCChannelList();
        for (byte channel = 0; channel < SimulationManager.SimulationChannelCount; channel++)
            response.Channels.Add(new SimulationChannel(0, channel, channel,
                _simulationManager.SimulationChannelPlayers(channel)));
        client.SendPacket(response);
        _logger.LogInformation("[SimulationCenter] Sent channel directory.");
        return true;
    }
}
