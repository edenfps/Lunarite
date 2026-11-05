using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AgentConnectHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(AgentConnectHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!ConnectRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(ConnectRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(ConnectRequest), packet);

        if (packet.Address != null) client.Session.LocalAddress = packet.Address;

        return true;
    }
}
