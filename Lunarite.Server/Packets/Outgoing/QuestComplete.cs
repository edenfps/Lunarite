using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// bd -- client opcode 49 dispatches OnQuestComplete(byte byAnswer, int nIndex).
public class QuestComplete : IPacketSerialize
{
    public byte Answer;
    public int QuestIndex;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)49);
        writer.Write(Answer);
        writer.Write(QuestIndex);
        return writer.Buffer;
    }
}
