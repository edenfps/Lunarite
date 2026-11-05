using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op223 ProcCharBattleRecord: sheet b, result b, native-struct byte count w, raw struct.
public sealed class CharBattleRecord : IPacketSerialize
{
    public byte Sheet;
    public byte Result;
    public byte[] Data = [];

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)223); w.Write(Sheet); w.Write(Result); w.Write((ushort)Data.Length);
        foreach (byte value in Data) w.Write(value);
        return w.Buffer;
    }
}
