using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// op56 SendQuestEnemydown(Id), sent on every quest-enemy kill by
// HuxleyEnemyCharacter.RequestItem (execSendQuestEnemydown @0x11149480 -> @0x111C2CB0 ->
// g_lpClientTcpLink->Send(56,"d",Id), gated on IsConnected()). The reply op59
// OnQuestListenDropItem fires Delegate_DropItem, which spawns HuxleyQuestPickup actors.
[PacketHandler]
public static class QuestEnemydownHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestLoot _questLoot = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestEnemydownHandler));

        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questLoot = serviceProvider.GetRequiredService<QuestLoot>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestDropRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("op56 SendQuestEnemydown: malformed payload.");
            return false;
        }
        int enemyId = packet.Index;
        int pid = Game.PidOf(client);
        int questIndex = _questRoomMembers.QuestOf(client.Id);
        int instanceToken = _questRoomMembers.InstanceOf(client.Id);
        if (!_questInstanceManager.IsQuestLootRequestAuthorized(pid, questIndex, instanceToken))
        {
            _logger.LogWarning("*** rejected op56 enemy {0}: PID {1} is not in an acquired quest instance.", enemyId, pid);
            client.SendPacket(new QuestListenDropItem { Opcode = 59, Pid = questIndex });
            return true;
        }
        // enemyId is HuxleyQuestEnemyData.EntryIndex (the numeric EnemyName); drops come from its
        // ItemDropGroupIndex.
        int difficulty = _questRoomMembers.DifficultyOf(client.Id);
        bool accepted = false;
        if (_questLoot.RollForEnemy(enemyId, difficulty) == null)
            _logger.LogWarning("*** rejected op56 Id={0}: enemy is absent from the exported enemy table.", enemyId);
        else if (!QuestInstanceManager.EnemyFitsQuest(_questLoot.EnemyLevel(enemyId),
                     _questInstanceManager.RunRecommendLevel(instanceToken)))
            _logger.LogWarning("*** rejected op56 Id={0}: level {1} enemy is above PID {2}'s quest {3} tier.",
                enemyId, _questLoot.EnemyLevel(enemyId), pid, questIndex);
        else if (!_questInstanceManager.TryAcceptEnemyDown(instanceToken, pid, _questLoot.EnemyXp(enemyId)))
            _logger.LogWarning("*** rejected op56 Id={0}: PID {1} exceeded the run's or the hour's kill-report budget.", enemyId, pid);
        else
        {
            accepted = true;
            _questInstanceManager.CommitQuestEntryOnReward(instanceToken, pid);
            _questStore.RecordEnemyDown(pid, questIndex, authenticatedDirectFieldRun: true);
            _questStore.RecordFieldEvent(pid, "FQOType_KillEnemy", enemyId);
        }
        int hostPid = _questInstanceManager.HostOf(instanceToken);
        if (!accepted)
        {
            // Empty reply; with guests present it stays ownerless as before.
            client.SendPacket(new QuestListenDropItem { Opcode = 59,
                Pid = _questRoomMembers.MemberCount(instanceToken) > 1 ? 0
                    : QuestInstanceManager.ResolveQuestPickupOwner(pid, questIndex, instanceToken, hostPid) });
            return true;
        }
        // One roll per participant, each dropped with its owner (see LootRecipients). An empty roll
        // is a valid retail outcome.
        foreach (int memberPid in _questInstanceManager.LootRecipients(instanceToken, pid))
        {
            var drops = _questLoot.RollForEnemy(enemyId, difficulty) ?? [];
            _logger.LogInformation("*** op56 SendQuestEnemydown Id={0} -> op59 PID {1}: {2}", enemyId, memberPid,
                drops.Count == 0 ? "empty" : string.Join(",", drops.Select(d => $"{d.Item1}x{d.Item2}")));
            var drop = new QuestListenDropItem { Opcode = 59,
                Pid = QuestInstanceManager.ResolveQuestPickupOwner(memberPid, questIndex, instanceToken, hostPid) };
            foreach (var (item, num) in drops) drop.Drops.Add(new QuestListenDropItem.Drop(item, num));
            QuestLootGrants.Issue(instanceToken, memberPid, drops.Select(x => (x.Item1, x.Item2)));
            client.SendPacket(drop);
        }
        return true;
    }
}
