using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class TransmitSend : IPacketSerialize
{
    public byte Answer;
    public byte ItemCell1 = byte.MaxValue;
    public byte ItemCell2 = byte.MaxValue;
    public byte ItemCell3 = byte.MaxValue;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)201);
        writer.Write(Answer);
        if (Answer == 0)
        {
            writer.Write(ItemCell1);
            writer.Write(ItemCell2);
            writer.Write(ItemCell3);
        }
        return writer.Buffer;
    }
}
