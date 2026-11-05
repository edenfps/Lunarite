using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// ddb  (client OnQuestStart @0x1110fd10 reads d,d,b -> PID, QuestIndex, Answer)
// Script event OnQuestStart(nPID, nAnswer, nQuestIndex) only reads nAnswer (error
// message when != 0); wire order matches every sibling quest packet: byte last.
public class QuestStart : IPacketSerialize
{
    public int Pid;
    public int QuestIndex;
    public byte Answer;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)54);

        writer.Write(Pid);
        writer.Write(QuestIndex);
        writer.Write(Answer);

        return writer.Buffer;
    }
}
