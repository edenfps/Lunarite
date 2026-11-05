namespace Lunarite.Packets.Outgoing;

public readonly record struct BlockWireEntry(int Pid, string Name, byte Level, byte Style, byte Tribe);
