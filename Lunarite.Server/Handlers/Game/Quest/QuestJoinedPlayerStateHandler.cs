using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

// Mission counterpart of op109: native sub_11108AB0 sends op52 `db` (member PID, player
// state) from the listen host when a joining member finishes UE3 login in MMO state QUEST (4);
// BATTLE (5) sends op109 instead. It confirms the guest arrived, so its held entry items are
// committed instead of refunded. No reply.
[PacketHandler]
public static class QuestJoinedPlayerStateHandler
{
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestJoinedPlayerStateHandler));
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int memberPid) || !reader.TryRead(out byte state)
            || reader.RemainingLength != 0) return false;
        int hostPid = Game.PidOf(client);
        if (!_questInstanceManager.TryGetGuestArrivals(hostPid, out var arrivals))
        {
            _logger.LogInformation("[QuestTransport] host PID {0} reported member PID {1} logged in (state {2}), no pending arrival.",
                hostPid, memberPid, state);
            return true;
        }
        bool expected;
        lock (arrivals)
        {
            expected = arrivals.Pending.Remove(memberPid);
            if (arrivals.Pending.Count == 0) _questInstanceManager.ForgetGuestArrivals(hostPid);
        }
        if (!expected)
        {
            _logger.LogWarning("[QuestTransport] host PID {0} reported unexpected member PID {1} (state {2}).",
                hostPid, memberPid, state);
            return true;
        }
        _questInstanceManager.CommitQuestGuestEntry(hostPid, memberPid);
        _logger.LogInformation("[QuestTransport] guest PID {0} reached host PID {1}, quest {2} (state {3}).",
            memberPid, hostPid, arrivals.QuestIndex, state);
        return true;
    }
}
