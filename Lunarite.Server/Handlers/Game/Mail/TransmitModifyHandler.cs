using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransmitModifyHandler
{
    private static TransmitStore _transmitStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _transmitStore = serviceProvider.GetRequiredService<TransmitStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!TransmitModifyRequest.TryDeserialize(ref reader, out var packet))
            return false;

        int pid = Game.PidOf(client);
        bool changed = _transmitStore.Modify(pid, packet.MessageSequence, packet.ModifyType);
        client.SendPacket(new TransmitModify
        {
            Answer = changed ? (byte)0 : (byte)8,
            MessageSequence = packet.MessageSequence,
            ModifyType = packet.ModifyType
        });
        return true;
    }
}
