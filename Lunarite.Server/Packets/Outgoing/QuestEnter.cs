using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// b
// db
public class QuestEnter : IPacketSerialize
{
    public byte Answer;

    public int QuestIndex;
    public byte Position;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)43);

        writer.Write(Answer);

        writer.Write(QuestIndex);
        writer.Write(Position);

        return writer.Buffer;
    }
}