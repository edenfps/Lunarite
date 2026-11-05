using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// dd
public class QuestEnterRequestNotice : IPacketSerialize
{
    public int Key;
    public int QuestIndex;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)44);

        writer.Write(Key);
        writer.Write(QuestIndex);

        return writer.Buffer;
    }
}
