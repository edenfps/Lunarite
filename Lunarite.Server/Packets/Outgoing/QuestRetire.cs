using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// bd -- client opcode 48 dispatches OnQuestRetire(byte byAnswer, int nIndex).
public class QuestRetire : IPacketSerialize
{
    public byte Answer;
    public int QuestIndex;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)48);
        writer.Write(Answer);
        writer.Write(QuestIndex);
        return writer.Buffer;
    }
}
