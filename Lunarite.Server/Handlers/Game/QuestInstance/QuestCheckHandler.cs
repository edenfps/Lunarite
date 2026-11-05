using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestCheckHandler
{
    private static QuestCatalog _questCatalog = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static QuestManager _questManager = null!;
    private static SquadManager _squadManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestCheckHandler));
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestCheckRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(QuestCheckRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(QuestCheckRequest), packet);

        int pid = Game.PidOf(client);
        if (!QuestInstanceManager.IsQuestDifficultyValid(packet.DifficultyLevel))
        {
            client.SendPacket(new QuestCheck { Pid = pid, QuestIndex = packet.Index, Answer = QuestResultCode.Error });
            _logger.LogWarning("[Quests] rejected invalid difficulty {0} for quest {1}, PID {2}.",
                packet.DifficultyLevel, packet.Index, pid);
            return true;
        }
        if (!_questManager.CanEnterQuest(pid, packet.Index))
        {
            client.SendPacket(new QuestCheck { Pid = pid, QuestIndex = packet.Index,
                Answer = _questManager.QuestUnavailableAnswer(pid, packet.Index) });
            _logger.LogWarning("[Quests] rejected gate check for unavailable quest {0}, PID {1}.", packet.Index, pid);
            return true;
        }
        var (members, leaderPid) = _squadManager.SquadOf(pid);
        if (leaderPid != pid)
        {
            client.SendPacket(new QuestCheck
            {
                Pid = pid, QuestIndex = packet.Index, Answer = QuestResultCode.NotSquadleader
            });
            return true;
        }

        var gateQuest = _questCatalog.Get(packet.Index);
        byte partySizeAnswer = QuestInstanceManager.ValidateQuestPartySize(gateQuest, members.Count);
        if (partySizeAnswer != 0)
        {
            client.SendPacket(new QuestCheck
                { Pid = pid, QuestIndex = packet.Index, Answer = partySizeAnswer });
            return true;
        }

        var preflight = members
            .Select(memberPid => new QuestSquadAnswers.Entry(memberPid, packet.Index,
                _questInstanceManager.PreflightQuestMember(memberPid, pid, packet.Index, gateQuest)))
            .ToList();
        if (members.Count > 1 && preflight.Any(x => x.Answer != 0))
        {
            client.SendPacket(new QuestSquadAnswers { Opcode = 220, Answers = preflight });
            return true;
        }
        // Solo: HuxleySubsystem.OnQuestCheck shows the RESULTTYPE_QUEST message (e.g. 13, an equipped
        // item at zero durability) and unlocks input. A later refusal at room creation would leave
        // the player at "Combat training will begin shortly".
        if (members.Count == 1 && preflight[0].Answer != 0)
        {
            client.SendPacket(new QuestCheck
                { Pid = pid, QuestIndex = packet.Index, Answer = preflight[0].Answer });
            return true;
        }

        _questInstanceManager.StartQuestDecision(pid, members, packet.Index, packet.DifficultyLevel);
        return true;
    }
}
