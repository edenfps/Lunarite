using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class TransmitGetCR : IPacketSerialize
{
    public byte Answer;
    public int MessageSequence;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)204);
        writer.Write(Answer);
        if (Answer == 0)
            writer.Write(MessageSequence);
        return writer.Buffer;
    }
}
