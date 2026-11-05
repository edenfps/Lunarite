using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestListenSpawnHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestManager _questManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestListenSpawnHandler));
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int requestedPid) || reader.RemainingLength != 0) return false;
        int selectedPid = Game.PidOf(client);
        int requesterQuest = _questRoomMembers.QuestOf(client.Id);
        int requesterInstance = _questRoomMembers.InstanceOf(client.Id);
        int requestedClientId = _questManager.QuestParticipantClientId(requestedPid);
        bool sameInstance = QuestManager.IsLocalQuestSpawnRequestAuthorized(
                selectedPid, requesterQuest, requesterInstance, requestedPid)
            || (requesterQuest > 0 && requesterInstance > 0
                && requestedClientId > 0
                && _questRoomMembers.QuestOf(requestedClientId) == requesterQuest
                && _questRoomMembers.InstanceOf(requestedClientId) == requesterInstance);
        client.SendPacket(new QuestListenSpawn
        {
            Pid = requestedPid,
            Answer = sameInstance ? (byte)0 : (byte)4 // RESULTTYPE_QUEST_NOTFOUND_PLAYER
        });
        _logger.LogInformation("[Quest] listen spawn request PID {0}, quest {1}: {2}.",
            requestedPid, requesterQuest, sameInstance ? "accepted" : "player not in instance");
        return true;
    }
}
