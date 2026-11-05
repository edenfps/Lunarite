using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Native US-client build 2010-06-21:
// SendQuestListenObjective -> Send(55, "ddbbb", PID, Quest, Objective, Count, Visible).
public sealed class QuestListenObjectiveRequest
{
    public int Pid;
    public int QuestIndex;
    public byte ObjectiveIndex;
    public byte Count;
    public byte Visible;

    public static bool TryDeserialize(ref PacketReader reader, out QuestListenObjectiveRequest packet)
    {
        packet = new QuestListenObjectiveRequest();
        return reader.TryRead(out packet.Pid)
            && reader.TryRead(out packet.QuestIndex)
            && reader.TryRead(out packet.ObjectiveIndex)
            && reader.TryRead(out packet.Count)
            && reader.TryRead(out packet.Visible)
            && reader.RemainingLength == 0;
    }
}
