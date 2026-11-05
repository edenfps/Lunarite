using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class PlayerRestoreHandler
{
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerRestoreHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!PlayerRestoreRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(PlayerRestoreRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(PlayerRestoreRequest), packet);
        bool restored = client.Session.AccountId is int userId
            && _characterStore.GetOwned(userId, packet.Pid) is not null
            && _characterStore.Restore(packet.Pid);
        client.SendPacket(new RestorePlayer
        {
            Answer = restored ? RestorePlayer.Success : RestorePlayer.Error,
            Pid = packet.Pid
        });
        return true;
    }
}
