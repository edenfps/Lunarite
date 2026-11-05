namespace Lunarite.Packets.Outgoing;

public readonly record struct BattleAxisEntry(byte Axis, byte Map, byte Battle,
    byte SapiPlayers, byte AlterPlayers, int EstimatedWaitMinutes, byte ReserveStatus);
