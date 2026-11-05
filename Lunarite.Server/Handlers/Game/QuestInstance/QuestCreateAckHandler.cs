using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestCreateAckHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestCreateAckHandler));
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestCreateAck.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(QuestCreateAck));
            return false;
        }

        int expected = _questRoomMembers.QuestOf(client.Id);
        int selectedPid = Game.PidOf(client);
        int acknowledgedQuest = QuestInstanceManager.ResolveQuestCreateAcknowledgment(
            packet.QuestIndex, packet.Ready, selectedPid, expected);
        if (acknowledgedQuest == 0)
        {
            _logger.LogWarning(
                "[Quest] unexpected create acknowledgment: client={0}, selected={1}, quest={2}, ready={3}, expected={4}.",
                client.Id, selectedPid, packet.QuestIndex, packet.Ready, expected);
            return true;
        }

        _logger.LogInformation("[Quest] PID {0} installed room data for quest {1} (wire identity {2}).",
            selectedPid, acknowledgedQuest, packet.QuestIndex);
        // Retail room lifecycle: Create (op42) -> CreateAck (op51) -> CanStart (op53).
        // HuxleyPCMMOHUD.OnQuestCanStart enables the ready controls for room quests and sends
        // QuestStart at once for quests without a room.
        client.SendPacket(new QuestCanStart { QuestIndex = acknowledgedQuest, CanStart = 1 });
        // Guests are sent later: the host has not loaded the map or opened its listen socket yet.
        // The op199 room notice marks that point, and entry items stay reserved until then.
        return true;
    }
}
