namespace Lunarite.Packets.Outgoing;

public readonly record struct FriendWireEntry(byte Relation, int Pid, string Name,
    byte Species, byte Level, byte Style, byte Login);
