namespace Lunarite.Packets.Outgoing;

public readonly record struct SimulationPlayerWire(int Pid, string Name, byte Level, byte Style,
    byte Rank, byte Squad, byte Species, byte State, byte FullConnection, byte Spectator,
    int PublicAddress, ushort PublicPort, int PrivateAddress, ushort PrivatePort, int ClanMark);
