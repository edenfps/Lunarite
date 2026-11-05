using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

// CClientTcpLink vtable slot 30 (sub_11108B60) sends client op199 "d" with op42's room PID
// (the quest index) once the hosted quest is running. No delegate, no reply. Unrelated to
// the server->client op199 mailbox list.
[PacketHandler]
public static class QuestRoomNoticeHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestRoomNoticeHandler));
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int roomPid) || reader.RemainingLength != 0) return false;
        int selectedPid = Game.PidOf(client);
        int activeQuest = _questRoomMembers.QuestOf(client.Id);
        int instanceToken = _questRoomMembers.InstanceOf(client.Id);
        bool valid = ChatManager.IsQuestRoomNoticeAuthorized(selectedPid, roomPid, activeQuest, instanceToken,
            _questInstanceManager.IsQuestLootRequestAuthorized(selectedPid, activeQuest, instanceToken));
        if (valid)
        {
            _logger.LogInformation("[Quest] client confirmed active room PID {0} for character {1}.", roomPid, selectedPid);
            _questInstanceManager.ConfirmQuestHostReady(selectedPid, activeQuest, QuestInstanceManager.QuestTransportSignal.HostRoomLive);
        }
        else
            _logger.LogWarning("[Quest] rejected room notice PID {0}; selected={1}, activeQuest={2}, instance={3}.",
                roomPid, selectedPid, activeQuest, instanceToken);
        return true;
    }
}
