using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestEnterHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestEnterHandler));
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestEnterRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(QuestEnterRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(QuestEnterRequest), packet);

        int questIndex = _questRoomMembers.QuestOf(client.Id);
        if (!client.Session.TryConsumeQuestEnterKey(packet.Key, questIndex))
        {
            _logger.LogWarning("[Quests] rejected QuestEnter key {0}: not issued by op44 for this connection's quest {1}.",
                packet.Key, questIndex);
            client.SendPacket(new QuestEnter { Answer = QuestResultCode.Error });
            return true;
        }

        var questEnter = new QuestEnter
        {
            Answer = 0,
            QuestIndex = questIndex,
            Position = 255
        };

        client.SendPacket(questEnter);

        return true;
    }
}
