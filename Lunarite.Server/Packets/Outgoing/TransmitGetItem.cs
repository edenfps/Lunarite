using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class TransmitGetItem : IPacketSerialize
{
    public byte Answer;
    public int MessageSequence;
    public int ItemSequence;
    public byte[] Item = new byte[32];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)203);
        writer.Write(Answer);
        if (Answer == 0)
        {
            writer.Write(MessageSequence);
            writer.Write(ItemSequence);
            writer.Write(Item);
        }
        return writer.Buffer;
    }
}
