namespace Lunarite.Packets.Outgoing;

// Decompiled ProcClanMember (`dsbbbbbbdbbd`) assigns the fields in this native order.
// MemberSpecies is not transmitted; the UI derives its label from the TypeIndex-shaped Tribe.
public readonly record struct ClanMemberWire(int Pid, string Name, byte Level, byte Tribe,
    byte Style, byte Grade, byte Login, byte MapState, int MapIndex,
    byte Veteran, byte Ranking, int MedalPoint);
