using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class PlayerSelectHandler
{
    private static CharacterManager _characterManager = null!;
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerSelectHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!PlayerSelectRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(PlayerSelectRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(PlayerSelectRequest), packet);

        if (client.Session.AccountId is not int userId
            || _characterStore.GetOwned(userId, packet.Pid) is not { } character)
        {
            _logger.LogWarning("[Accounts] rejected selection of unowned PID {0} by Game client {1}.", packet.Pid, client.Id);
            return true;
        }
        // HuxleyMMOSelectPlayerMenu.ConnectServer only selects when fDelRemainTime == 0.
        if (character.DeleteAtUnixSeconds > 0)
        {
            _logger.LogWarning("[Accounts] rejected selection of PID {0} pending deletion by Game client {1}.",
                packet.Pid, client.Id);
            return true;
        }

        int selectionUnixSeconds = checked((int)Math.Min(int.MaxValue, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _characterManager.BindSelectedCharacter(client, packet.Pid, selectionUnixSeconds,
            out var expiredItems, out var expiredAttachments);
        // Account-wide bank: the selected character takes over the other characters' bank items.
        int bankMoved = _playerStore.GatherAccountBank(packet.Pid,
            _characterStore.All(userId).Select(other => other.Pid));
        if (bankMoved > 0)
            _logger.LogInformation("[Bank] PID {0} took {1} bank item line(s) from the account's other characters.",
                packet.Pid, bankMoved);
        client.SendPacket(new SelectPlayer { Pid = packet.Pid });

        var entryMap = _characterManager.ResolveEntryMap(client, packet.Pid, packet.DirectSimul);
        Game.PlayerFor(packet.Pid).CityMap = entryMap.Map;

        _characterManager.SendSelectedPlayerState(client, packet.Pid, selectionUnixSeconds);
        CityManager.ArmExpectedCityTravel(client);
        client.SendPacket(new LoadMap { Index = entryMap.Map, Position = entryMap.Area });
        CharacterManager.SendExpirationNotices(client, expiredItems, expiredAttachments);
        return true;
    }
}
