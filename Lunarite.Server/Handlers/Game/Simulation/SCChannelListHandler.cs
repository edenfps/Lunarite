using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SCChannelListHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(SCChannelListHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0)
            return false;

        var response = new SCChannelList();
        // Region 0 / group 0 / channel 0 are valid enum roots in the June client.
        response.Channels.Add(new SimulationChannel(0, 0, 0, 0));
        client.SendPacket(response);
        _logger.LogInformation("[SimulationCenter] Sent channel directory.");
        return true;
    }
}
