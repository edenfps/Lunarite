using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;

namespace Lunarite.Handlers;

[PacketHandler]
public static class P2PNatEndpointReportHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(P2PNatEndpointReportHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!P2PNatEndpointReport.TryDeserialize(ref reader, out var packet)) return false;
        // The native reporter only emits NAT types 1..3 after a successful test. The result is
        // keyed by the Game link, not a claimed PID. GetLocalHostAddr can leave the local IP
        // zero; the endpoint builder then falls back to the Game connection address.
        if (client.Session.AccountId == null || packet.NatType is < 1 or > 3
            || packet.PublicAddress == 0
            || packet.PublicPort == 0 || packet.LocalPort == 0)
        {
            _logger.LogWarning("[P2P] rejected unauthenticated or invalid NAT endpoint report from client {0}.", client.Id);
            return true;
        }
        client.Session.NatEndpoint = packet;
        // Native ProcNATTypeResult (server op123) clears the NAT-test queues and ends the test
        // phase. Result zero invokes OnConnectError.
        client.SendPacket(new NATTypeResult { Result = 1 });
        _logger.LogInformation("[P2P] client {0} reported NAT type {1}, public UDP {2}, local UDP {3}.",
            client.Id, packet.NatType, packet.PublicPort, packet.LocalPort);
        return true;
    }
}
