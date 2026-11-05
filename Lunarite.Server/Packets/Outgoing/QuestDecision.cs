using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// ddb
public class QuestDecision : IPacketSerialize
{
    public int Pid;
    public int QuestIndex;
    public byte DifficultyLevel;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)41);

        writer.Write(Pid);
        writer.Write(QuestIndex);
        writer.Write(DifficultyLevel);

        return writer.Buffer;
    }
}
