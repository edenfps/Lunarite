using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class VisibleHandler
{
    private static CharacterManager _characterManager = null!;
    private static CityManager _cityManager = null!;
    private static ILogger _logger = null!;
    private static PlayerManager _playerManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(VisibleHandler));

        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!VisibleRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(VisibleRequest));
            return false;
        }

        int pid = Game.PidOf(client);
        if (pid <= 0) return true;

        if (!packet.IsVisible)
        {
            if (Game.PlayerFor(pid).TryClearSpawned())
                _cityManager.BroadcastPresence(client, new DelPlayer { Pid = pid });
            return true;
        }

        if (!Game.PlayerFor(pid).TryMarkSpawned()) return true;
        int[] missed = Game.PlayerFor(pid).TakeMissedPresence();
        foreach (var pair in _playerManager.Online)
        {
            int otherPid = pair.Key;
            if (otherPid == pid || !_cityManager.SharesPresence(client, pair.Value)) continue;
            if (!Game.PlayerFor(otherPid).IsSpawned)
            {
                Game.PlayerFor(otherPid).NoteMissedPresence(pid);
                continue;
            }
            _characterManager.SendOtherPlayerSnapshot(pair.Value, pid);
            if (missed.Contains(otherPid)) _characterManager.SendOtherPlayerSnapshot(client, otherPid);
        }

        return true;
    }
}
