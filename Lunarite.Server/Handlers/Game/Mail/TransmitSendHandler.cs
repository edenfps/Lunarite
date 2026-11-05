using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransmitSendHandler
{
    private static TransmitManager _transmitManager = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _transmitManager = serviceProvider.GetRequiredService<TransmitManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!TransmitSendRequest.TryDeserialize(ref reader, out var packet))
            return false;

        int senderPid = Game.PidOf(client);
        var sender = _characterStore.Get(senderPid);
        var recipient = _transmitManager.ResolveTransmitRecipient(packet.Pid, packet.ToName);
        if (sender == null || recipient == null)
            client.SendPacket(new TransmitSend { Answer = TransmitResultCode.NotfoundPlayer });
        else if (sender.Pid == recipient.Pid)
            client.SendPacket(new TransmitSend { Answer = TransmitResultCode.Self });
        else if (!_transmitManager.CanSendTransmit(sender.Pid, recipient.Pid))
            client.SendPacket(new TransmitSend { Answer = TransmitResultCode.Blockedplayer });
        else if (packet.CR < 0)
            client.SendPacket(new TransmitSend { Answer = TransmitResultCode.NotEnoughMoney });
        else
            _transmitManager.TransmitSend(client, packet, sender, recipient);
        return true;
    }
}
