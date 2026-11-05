using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class GotoCharMenuHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static CharacterManager _characterManager = null!;
    private static CityManager _cityManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(GotoCharMenuHandler));
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        _cityManager.LeaveCurrentPresence(client, pid);
        if (pid != 0 && Game.TryGetClient(pid, out var current) && ReferenceEquals(current, client))
        {
            // The Game link stays open (HuxleyMMOGameViewportClient.OpenSelectPlayerScene), so
            // the disconnect release never runs: the character logs out here instead.
            Game.PlayerFor(pid).TryClearSummonedVehicle(out _);
            DisconnectHandler.ReleaseForCharacterSelect(client);
        }
        else
        {
            // A stale link for a character now played elsewhere only drops its own binding.
            _questRoomMembers.ClearQuest(client.Id);
            client.Player = null;
        }
        _characterManager.SendPlayerList(client);
        _logger.LogInformation("[Session] Client {0} returned to character selection.", client.Id);
        return true;
    }
}
