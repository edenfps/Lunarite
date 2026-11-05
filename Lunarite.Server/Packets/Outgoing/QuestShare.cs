using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnQuestShare decodes opcode 46 as `ddb`.
public sealed class QuestShare : IPacketSerialize
{
    public int Pid;
    public int QuestIndex;
    public byte Answer;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)46);
        writer.Write(Pid);
        writer.Write(QuestIndex);
        writer.Write(Answer);
        return writer.Buffer;
    }
}
