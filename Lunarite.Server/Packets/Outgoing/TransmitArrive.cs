using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class TransmitArrive : IPacketSerialize
{
    public byte TransmitType;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)205);
        writer.Write(TransmitType);
        return writer.Buffer;
    }
}
