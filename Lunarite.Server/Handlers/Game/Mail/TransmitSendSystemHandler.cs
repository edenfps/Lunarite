using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransmitSendSystemHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;
    private static TransmitManager _transmitManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(TransmitSendSystemHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _transmitManager = serviceProvider.GetRequiredService<TransmitManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!TransmitSendSystemRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(TransmitSendSystemRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(TransmitSendSystemRequest), packet);

        var recipient = _characterStore.All().FirstOrDefault(x =>
            string.Equals(x.Name, packet.ToName, StringComparison.OrdinalIgnoreCase));
        if (recipient == null)
        {
            client.SendPacket(new TransmitSend { Answer = TransmitResultCode.NotfoundPlayer });
            return true;
        }

        int senderPid = Game.PidOf(client);
        int senderQuest = _questRoomMembers.QuestOf(client.Id);
        int senderInstance = _questRoomMembers.InstanceOf(client.Id);
        int recipientClientId = Game.TryGetClient(recipient.Pid, out var recipientClient)
            ? recipientClient.Id : _questRoomMembers.DetachedClientOf(recipient.Pid);
        if (!TransmitManager.CanDeliverScriptedQuestMail(packet.TransmitType, senderPid, recipient.Pid,
                senderQuest, senderInstance, _questRoomMembers.QuestOf(recipientClientId),
                _questRoomMembers.InstanceOf(recipientClientId)))
        {
            _logger.LogWarning("[Mail] rejected scripted mail from PID {0} to PID {1}, type {2}: no shared quest authority.",
                senderPid, recipient.Pid, packet.TransmitType);
            client.SendPacket(new TransmitSend { Answer = TransmitResultCode.Error });
            return true;
        }

        // Scripted quest mail is authority-gated above. HuxleySeqAct_SendMessage never passes
        // nUnique, so it arrives with ID 0 and cannot be deduplicated; a per-sender throttle and a
        // per-recipient daily limit bound it instead of the player-mail mailbox cap.
        var delivery = _transmitManager.TransmitSendSystem(senderPid, recipient.Pid, packet);
        if (!delivery.Created && delivery.Answer != 10)
            _logger.LogWarning("[Mail] refused scripted mail from PID {0} to PID {1}: throttled or daily limit.",
                senderPid, recipient.Pid);
        client.SendPacket(new TransmitSend { Answer = delivery.Answer });
        if (delivery.Created) TransmitManager.NotifyTransmitArrival(recipient.Pid, packet.TransmitType);

        return true;
    }
}
