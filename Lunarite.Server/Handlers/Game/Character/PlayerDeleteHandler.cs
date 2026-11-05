using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class PlayerDeleteHandler
{
    private static ILogger _logger = null!;
    private static AuctionStore _auctionStore = null!;
    private static CharacterStore _characterStore = null!;
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerDeleteHandler));

        _auctionStore = serviceProvider.GetRequiredService<AuctionStore>();
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!PlayerDeleteRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(PlayerDeleteRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(PlayerDeleteRequest), packet);

        if (client.Session.AccountId is not int userId
            || _characterStore.GetOwned(userId, packet.Pid) is null)
        {
            client.SendPacket(new DeletePlayer { Answer = DeletePlayer.NotFound });
            return true;
        }

        // DeleteMsg[3] "already deleted": a pending deletion keeps its timer.
        if (_characterStore.Get(packet.Pid) is { DeleteAtUnixSeconds: > 0 })
        {
            client.SendPacket(new DeletePlayer { Answer = DeletePlayer.AlreadyDeleted });
            return true;
        }

        // Retail answer 5: a clan master must hand over leadership before deleting.
        if (_clanStore.IsMaster(packet.Pid))
        {
            client.SendPacket(new DeletePlayer { Answer = DeletePlayer.ClanMasterMustEntrust });
            return true;
        }

        // No auction-specific result exists; answer 1 is the generic failure.
        if (_auctionStore.HasListings(packet.Pid))
        {
            client.SendPacket(new DeletePlayer { Answer = DeletePlayer.Error });
            _logger.LogWarning("[Chars] refused deletion of PID {0} with auction escrow.", packet.Pid);
            return true;
        }

        int deleteMinutes = 5;
        if (int.TryParse(Environment.GetEnvironmentVariable("CHARACTER_DELETE_MINUTES"), out int configured))
            deleteMinutes = Math.Clamp(configured, 1, byte.MaxValue);
        byte remaining = _characterStore.ScheduleDelete(packet.Pid, deleteMinutes);

        client.SendPacket(new DeletePlayer
        {
            Answer = DeletePlayer.Success,
            Pid = packet.Pid,
            DeleteFlag = DeletePlayer.DeleteQueued,
            RemainingTime = remaining
        });
        _logger.LogInformation("[Chars] queued PID {0} for deletion and reported {1} minute(s).", packet.Pid, remaining);
        return true;
    }
}
