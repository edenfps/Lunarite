using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnTransportEnter, server op118: `b`.
public sealed class TransportEnter : IPacketSerialize
{
    public byte Answer;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)118); writer.Write(Answer);
        return writer.Buffer;
    }
}
