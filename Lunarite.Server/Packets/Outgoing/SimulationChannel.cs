namespace Lunarite.Packets.Outgoing;

public readonly record struct SimulationChannel(byte Region, byte Group, byte Id, int PlayerCount);
