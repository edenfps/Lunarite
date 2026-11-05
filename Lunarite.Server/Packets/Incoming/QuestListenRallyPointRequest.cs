using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// SendQuestListenRallyPoint ->
// Send(54, "ddbbbdddddd", PID, Quest, Rally, MaxRally, Complete,
//      SquadBP, BP, QT, KilledMonsters, SpawnedMonsters, PlayTime).
public sealed class QuestListenRallyPointRequest
{
    public int Pid;
    public int QuestIndex;
    public byte RallyPoint;
    public byte MaxRallyPoint;
    public byte Complete;
    public int SquadBP;
    public int BP;
    public int QT;
    public int KilledMonsters;
    public int SpawnedMonsters;
    public int PlayTime;

    public static bool TryDeserialize(ref PacketReader reader, out QuestListenRallyPointRequest packet)
    {
        packet = new QuestListenRallyPointRequest();
        return reader.TryRead(out packet.Pid)
            && reader.TryRead(out packet.QuestIndex)
            && reader.TryRead(out packet.RallyPoint)
            && reader.TryRead(out packet.MaxRallyPoint)
            && reader.TryRead(out packet.Complete)
            && reader.TryRead(out packet.SquadBP)
            && reader.TryRead(out packet.BP)
            && reader.TryRead(out packet.QT)
            && reader.TryRead(out packet.KilledMonsters)
            && reader.TryRead(out packet.SpawnedMonsters)
            && reader.TryRead(out packet.PlayTime)
            && reader.RemainingLength == 0;
    }
}
