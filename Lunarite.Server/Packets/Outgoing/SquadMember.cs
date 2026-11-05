namespace Lunarite.Packets.Outgoing;

public readonly record struct SquadMember(string SquadName, int Pid, byte Rank, byte Style,
    string Name, ushort Level, byte IsLeader, byte TribeRank, byte MapIndex, int Ping);
