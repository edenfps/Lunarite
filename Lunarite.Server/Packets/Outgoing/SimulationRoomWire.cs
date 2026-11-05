namespace Lunarite.Packets.Outgoing;

public readonly record struct SimulationRoomWire(byte Id, string Name, string Owner, int Map,
    byte GameType, byte MaxPlayers, byte GoalTime, ushort GoalScore, byte IsPublic,
    byte RemainTime, byte GameState, byte LimitClass, byte ChoiceTribe);
