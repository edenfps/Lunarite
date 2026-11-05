using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op151 passes one aligned 12-byte _BattleZoneInfo_ blob. Its
// first six bytes are the exported fields, bytes 6..7 are struct padding, and
// the leader PID occupies offset 8.
public sealed class BRTZoneInfo : IPacketSerialize
{
    public byte MaxTeam;
    public byte Sapiens;
    public byte Alternatives;
    public byte ReadyButtonOn;
    public byte EnemyState;
    public byte Started;
    public int LeaderPid;

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)151);
        w.Write(MaxTeam); w.Write(Sapiens); w.Write(Alternatives); w.Write(ReadyButtonOn);
        w.Write(EnemyState); w.Write(Started); w.Write((ushort)0); w.Write(LeaderPid);
        return w.Buffer;
    }
}
