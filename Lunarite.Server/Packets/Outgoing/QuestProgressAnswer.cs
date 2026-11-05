using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op56 OnQuestListenRallyPoint and op58 OnQuestListenObjectiveComplete both parse
// bdbbb: answer, quest index, sub-index, count/max, visible/complete.
public sealed class QuestProgressAnswer : IPacketSerialize
{
    public byte Opcode;
    public byte Answer;
    public int QuestIndex;
    public byte Index;
    public byte Count;
    public byte State;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write(Opcode); writer.Write(Answer); writer.Write(QuestIndex);
        writer.Write(Index); writer.Write(Count); writer.Write(State);
        return writer.Buffer;
    }
}
