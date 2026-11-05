using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;

namespace Lunarite.Handlers;

[PacketHandler]
public static class P2PHolePunchAcknowledgmentHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(P2PHolePunchAcknowledgmentHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte result) || reader.RemainingLength != 0) return false;
        // FP2PSocket::RecvTestACKHolePunchingPacket sends client op118 "b" once,
        // with value 1, after its UDP test ACK. This is not UE3 guest admission.
        if (client.Session.AccountId == null || result != 1)
        {
            _logger.LogWarning("[P2P] rejected hole-punch ACK from client {0}: result {1}.", client.Id, result);
            return true;
        }
        client.Session.HolePunchAcknowledged = true;
        _logger.LogInformation("[P2P] client {0} confirmed UDP hole-punch test ACK.", client.Id);
        return true;
    }
}
