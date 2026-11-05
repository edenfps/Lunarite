using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestDecisionAnswerHandler
{
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestDecisionAnswerHandler));
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestCheckRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", "QuestDecisionAnswer");
            return false;
        }

        byte answer = packet.DifficultyLevel;
        _logger.LogDebug("Received {0} packet. ( Index={1} Answer={2} )", "QuestDecisionAnswer", packet.Index, answer);

        // ANSWER_CONSENT=0. On consent the master creates the room and answers with op42
        // OnQuestCreate. op52 Answer=1 here shows QuestMessage[1] ("There has been an error in
        // the quest task.").
        if (!_questInstanceManager.TryGetPendingDecision(client.Id, out var pending)
            || pending.QuestIndex != packet.Index)
        {
            _logger.LogWarning("[Quest] ignored decision answer for quest {0}: no matching pending gate decision.", packet.Index);
            return true;
        }

        int pid = Game.PidOf(client);
        bool completed = false;
        lock (pending)
        {
            if (pending.Finalized || !pending.Members.Contains(pid) || pending.Answers.ContainsKey(pid)) return true;
            pending.Answers[pid] = answer;
            _logger.LogInformation("[QuestTransport] consent PID {0}, quest {1}, answer {2}, responses {3}/{4}.",
                pid, pending.QuestIndex, answer, pending.Answers.Count, pending.Members.Count);
            _questInstanceManager.ForgetPendingDecision(client.Id);
            if (pending.Answers.Count == pending.Members.Count)
            {
                pending.Finalized = true;
                completed = true;
            }
        }

        if (completed) _questInstanceManager.FinalizePendingQuestDecision(pending);

        return true;
    }
}
