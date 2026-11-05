using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op17 OnReceivedAnotherPlayerInfo: result b; on success sbdbbdw + 32 raw bytes.
public sealed class ReceivedAnotherPlayerInfo : IPacketSerialize
{
    public byte Result;
    public string Name = "";
    public byte Species;
    public int Level;
    public byte Style, Rank;
    public int ClanMark;
    public byte[] BattleRecord = new byte[32];

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)17); w.Write(Result);
        if (Result != 0) return w.Buffer;
        w.Write(Name); w.Write(Species); w.Write(Level); w.Write(Style); w.Write(Rank); w.Write(ClanMark);
        w.Write((ushort)BattleRecord.Length);
        foreach (byte value in BattleRecord) w.Write(value);
        return w.Buffer;
    }
}
