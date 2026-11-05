using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SpawnHandler
{
    private static CharacterManager _characterManager = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static CityManager _cityManager = null!;
    private static QuestManager _questManager = null!;
    private static ILogger _logger = null!;
    private static PlayerManager _playerManager = null!;
    private static TransmitStore _transmitStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {

        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(SpawnHandler));

        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _transmitStore = serviceProvider.GetRequiredService<TransmitStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!SpawnRequest.TryDeserialize(ref reader, out var spawn)) return false;
        _logger.LogDebug("Received {0} packet.", "Spawn");

        int pid = Game.PidOf(client);
        client.Session.ExpectedCityTravelAt = null;
        if (pid <= 0) return true;
        Game.PlayerFor(pid).Position = (spawn.X, spawn.Y, spawn.Z, unchecked((short)spawn.Yaw));
        Game.PlayerFor(pid).MarkSpawned();
        foreach (var pair in _playerManager.Online)
        {
            int otherPid = pair.Key;
            Client otherClient = pair.Value;
            if (otherPid == pid || !Game.PlayerFor(otherPid).IsSpawned
                || !_cityManager.SharesPresence(client, otherClient)) continue;
            _characterManager.SendOtherPlayerSnapshot(client, otherPid);
            _characterManager.SendOtherPlayerSnapshot(otherClient, pid);
        }

        // HuxleyPCMMOHUD hides the new-mail marker during init, and OnSpawn is the first callback
        // after it. One op205 per unread type is enough: it colors a single marker.
        if (client.Session.TryPublishMailboxArrival())
            foreach (byte transmitType in _transmitStore.UnreadTypes(pid))
                client.SendPacket(new TransmitArrive { TransmitType = transmitType });

        _questManager.FlushQuestTravelRefresh(client, pid);
        _questInstanceManager.OnCitySpawn(pid);
        _characterManager.StartPendingTutorial(client, pid);

        return true;
    }
}
