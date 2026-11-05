using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class VerifyHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(VerifyHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!VerifyRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(VerifyRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(VerifyRequest), packet);

        int authenticatedUid = (client.Session.AccountId ?? 0);
        if (!CharacterManager.IsGameVerifyAuthorized(packet.IsPC, packet.UID, authenticatedUid))
        {
            _logger.LogWarning("[Accounts] rejected Game verify on client {0}: claimed UID {1}, authenticated UID {2}, IsPC={3}.",
                client.Id, packet.UID, authenticatedUid, packet.IsPC);
            client.Dispose();
        }

        return true;
    }
}
