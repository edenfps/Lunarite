using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Handlers;

// op57 = SendDropItemGroup(groupIndex), immediately following SendQuestEnemydown in the
// exported native declaration order. HuxleyItemFactory arms Delegate_DropItem_Group after this
// call, so the matching response is op60 OnQuestListenDropItem_Group, not op59.
[PacketHandler]
public static class DropItemGroupHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestLoot _questLoot = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(DropItemGroupHandler));
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questLoot = serviceProvider.GetRequiredService<QuestLoot>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestDropRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("op57 SendDropItemGroup: malformed payload.");
            return false;
        }

        int pid = Game.PidOf(client);
        int questIndex = _questRoomMembers.QuestOf(client.Id);
        int instanceToken = _questRoomMembers.InstanceOf(client.Id);
        if (!_questInstanceManager.IsQuestLootRequestAuthorized(pid, questIndex, instanceToken)
            || !QuestLoot.IsScriptedGroup(packet.Index) || !_questLoot.HasGroup(packet.Index))
        {
            _logger.LogWarning(
                "*** rejected op57 group {0}: PID {1} has no active quest or group is not scripted.",
                packet.Index, pid);
            client.SendPacket(new QuestListenDropItem { Opcode = 60, Pid = questIndex });
            return true;
        }
        if (!_questInstanceManager.TryAcceptScriptedDrop(instanceToken, pid))
        {
            _logger.LogWarning("*** rejected op57 group {0}: PID {1} exceeded the run's or the hour's scripted drop count.",
                packet.Index, pid);
            client.SendPacket(new QuestListenDropItem { Opcode = 60, Pid = questIndex });
            return true;
        }
        _questInstanceManager.CommitQuestEntryOnReward(instanceToken, pid);
        int difficulty = _questRoomMembers.DifficultyOf(client.Id);
        int hostPid = _questInstanceManager.HostOf(instanceToken);
        // One roll per participant, each dropped with its owner (QuestInstanceManager.LootRecipients).
        foreach (int memberPid in _questInstanceManager.LootRecipients(instanceToken, pid))
        {
            var drops = _questLoot.Roll(packet.Index, difficulty);
            _logger.LogInformation("*** op57 SendDropItemGroup group={0} -> op60 PID {1}: {2}", packet.Index, memberPid,
                drops.Count == 0 ? "empty" : string.Join(",", drops.Select(d => $"{d.item}x{d.num}")));
            var reply = new QuestListenDropItem { Opcode = 60,
                Pid = QuestInstanceManager.ResolveQuestPickupOwner(memberPid, questIndex, instanceToken, hostPid) };
            foreach (var (item, num) in drops) reply.Drops.Add(new QuestListenDropItem.Drop(item, num));
            QuestLootGrants.Issue(instanceToken, memberPid, drops.Select(x => (x.item, x.num)));
            client.SendPacket(reply);
        }
        return true;
    }
}
