using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestStartHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestStartHandler));
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestIndexRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(QuestIndexRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(QuestIndexRequest), packet);

        int activeQuest = _questRoomMembers.QuestOf(client.Id);
        int activeInstance = _questRoomMembers.InstanceOf(client.Id);
        bool authorized = QuestInstanceManager.IsQuestStartAuthorized(packet.QuestIndex, activeQuest, activeInstance);
        var questStart = new QuestStart
        {
            Pid = Game.PidOf(client),
            Answer = authorized ? (byte)0 : (byte)1,
            QuestIndex = packet.QuestIndex
        };

        client.SendPacket(questStart);
        if (!authorized)
            _logger.LogWarning("[Quest] rejected stale start request {0}; active quest={1}, instance={2}.",
                packet.QuestIndex, activeQuest, activeInstance);

        return true;
    }
}
