using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// ddb
public class QuestCheck : IPacketSerialize
{
    public int Pid;
    public int QuestIndex;
    public byte Answer;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)40);

        writer.Write(Pid);
        writer.Write(QuestIndex);
        writer.Write(Answer);

        return writer.Buffer;
    }
}
