using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BRTLeaveHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static BattleManager _battleManager = null!;
    private static SimulationManager _simulationManager = null!;
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BRTLeaveHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native SendBRTLeave op106 is an `m` blob containing four 32-bit metrics:
        // average/max ping followed by average/min FPS.
        if (!reader.TryRead(out int averagePing) || !reader.TryRead(out int maxPing)
            || !reader.TryRead(out int averageFps) || !reader.TryRead(out int minFps)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        if (pid > 0) _battleManager.CancelReservation(pid);
        _logger.LogInformation("[BRT] PID {0} left (ping {1}/{2}, FPS {3}/{4}).",
            pid, averagePing, maxPing, averageFps, minFps);

        // The quest HUD's ABORT MISSION also sends op106 (seen from a retail client in a forest
        // instance), so inside a quest it leaves the quest.
        int activeQuest = _questRoomMembers.QuestOf(client.Id);
        if (QuestManager.IsQuestAbortLeave(activeQuest))
            _questInstanceManager.QuestLeave(client, -1, -1, "aborted");

        // Simulation clients display the final scoreboard for ten seconds, then send op106.
        // Retail answers by sending them back to their faction Simulation Center. Room membership
        // is kept: the owner already reset the room to READY through op115.
        var (returnToSimulation, abortedMembers) = _simulationManager.AbortHostedMatch(pid);
        if (abortedMembers.Count != 0)
        {
            foreach (var member in abortedMembers)
            {
                int map = CharacterStore.SimulationCenterMap(_characterStore.Get(member.Pid)?.Index ?? 1);
                Game.PlayerFor(member.Pid).CityMap = map;
                member.Client.SendPacket(new LoadMap { Index = map, Position = 0 });
            }
        }
        else if (returnToSimulation)
        {
            int map = CharacterStore.SimulationCenterMap(_characterStore.Get(pid)?.Index ?? 1);
            Game.PlayerFor(pid).CityMap = map;
            client.SendPacket(new LoadMap { Index = map, Position = 0 });
            _logger.LogInformation("[Simulation] PID {0} returning to Simulation Center map {1} after results.",
                pid, map);
        }
        return true;
    }
}
