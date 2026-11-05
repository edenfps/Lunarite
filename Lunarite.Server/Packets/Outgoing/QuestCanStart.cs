using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// db
public class QuestCanStart : IPacketSerialize
{
    public int QuestIndex;
    public byte CanStart;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)53);

        writer.Write(QuestIndex);
        writer.Write(CanStart);

        return writer.Buffer;
    }
}
