using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanMemberHandler
{
    private static BattleInstanceManager _battleInstanceManager = null!;
    private static CharacterManager _characterManager = null!;
    private static QuestManager _questManager = null!;
    private static SimulationManager _simulationManager = null!;
    private static CharacterStore _characterStore = null!;
    private static ClanStore _clanStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
        _battleInstanceManager = serviceProvider.GetRequiredService<BattleInstanceManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var clan = _clanStore.ForPid(pid);
        var response = new ClanMember { Result = clan == null ? (byte)49 : (byte)13 }; // 49 CLAN_NOJOINED
        if (clan != null)
        {
            var contributionRanks = clan.Members.ToDictionary(
                member => member.Pid,
                member => (byte)Math.Clamp(
                    1 + clan.Members.Count(other => other.Contribution > member.Contribution),
                    1,
                    byte.MaxValue));
            foreach (var member in clan.Members)
            {
                var character = _characterStore.Get(member.Pid);
                if (character == null) continue;
                var progress = _playerStore.GetProgress(member.Pid);
                bool online = _characterManager.IsPlayerOnline(member.Pid);
                bool inQuest = _questManager.IsPlayerInQuest(member.Pid);
                bool inSimulation = _simulationManager.IsInRoom(member.Pid);
                int mapState = ClanManager.ClanMapState(online, inQuest, _battleInstanceManager.IsInBattle(member.Pid),
                    inSimulation);
                int map = (Game.PlayerFor(member.Pid).CityMap ?? _characterStore.GetCityLocation(member.Pid).Map);
                response.Members.Add(new ClanMemberWire(member.Pid, character.Name,
                    (byte)Math.Clamp(progress.Level, 0, 255), character.Index, character.Style,
                    member.Grade, online ? (byte)1 : (byte)0, (byte)mapState,
                    map, 0, contributionRanks[member.Pid], member.Contribution));
            }
        }
        client.SendPacket(response);
        return true;
    }
}
