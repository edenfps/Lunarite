using System;
using System.Linq;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;

namespace Lunarite.Handlers;

[PacketHandler]
public static class P2PNetworkAddressesHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(P2PNetworkAddressesHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!P2PNetworkAddressesReport.TryDeserialize(ref reader, out var packet)) return false;
        if (client.Session.AccountId == null)
        {
            _logger.LogWarning("[P2P] rejected unauthenticated network-address report from client {0}.", client.Id);
            return true;
        }
        // Relay-container report (op196). Kept separately so it never overrides the op155 endpoint.
        client.Session.NetworkAddresses = packet;
        _logger.LogInformation("[P2P] client {0} reported {1} relay/my endpoint pair(s): {2}.",
            client.Id, packet.Entries.Count,
            string.Join(", ", packet.Entries.Select(x =>
                $"{new IPAddress(unchecked((uint)x.RelayAddress))}:{x.RelayPort}" +
                $" -> {new IPAddress(unchecked((uint)x.MyAddress))}:{x.MyPort}")));
        return true;
    }
}
