namespace Lunarite.Packets.Outgoing;

public readonly record struct BattleFieldEntry(byte Map, byte Status, byte SapiOccupy,
    byte SapiAttack, byte AlterAttack, byte BattleTime, byte NextBattleTime,
    byte SapiPlayers, byte AlterPlayers);
