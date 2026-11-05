using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SquadNotifyHandler
{
    private static QuestManager _questManager = null!;
    private static SquadManager _squadManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(SquadNotifyHandler));
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int targetPid) || !reader.TryRead(out byte type)
            || !reader.TryRead(out byte result) || reader.RemainingLength != 0) return false;
        int senderPid = Game.PidOf(client);
        if (senderPid <= 0) return true;
        List<int> members = _squadManager.SquadMembersOf(senderPid);
        if (!SquadManager.CanRelaySquadNotify(senderPid, targetPid, members))
        {
            _logger.LogWarning("[Squad] rejected notify type {0} result {1} from PID {2} to non-member PID {3}.",
                type, result, senderPid, targetPid);
            return true;
        }
        // The active HuxleyQuestShareMenu sends its own PID here, not the sharer's PID.
        // Type 0 share notifications describe the sender to the whole squad; the menu's
        // older SendQuestShareConsent branch is commented out in the exported script.
        if (type == 0 && targetPid == senderPid && result is >= 1 and <= 8)
        {
            if (result != 1) _questManager.TryTakePendingShare(client.Id, out _);
            foreach (int memberPid in members)
                if (Game.TryGetClient(memberPid, out var memberClient))
                    memberClient.SendPacket(new SquadNotify { Pid = senderPid, Type = type, Result = result });
            return true;
        }
        if (Game.TryGetClient(targetPid, out var target))
            target.SendPacket(new SquadNotify { Pid = senderPid, Type = type, Result = result });
        else
            _logger.LogInformation("[Squad] notify type {0} result {1} from PID {2} targets offline PID {3}.",
                type, result, senderPid, targetPid);
        return true;
    }
}
