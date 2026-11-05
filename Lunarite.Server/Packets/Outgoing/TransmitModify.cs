using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class TransmitModify : IPacketSerialize
{
    public byte Answer;
    public int MessageSequence;
    public byte ModifyType;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)202);
        writer.Write(Answer);
        if (Answer == 0)
        {
            writer.Write(MessageSequence);
            writer.Write(ModifyType);
        }
        return writer.Buffer;
    }
}
