using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnLoadMap @ 0x11125990 reads `bb`: map index and area/player-start index.
// Script INDEX_NONE (-1) is carried as 0xFF, matching client SendMoveMap's byte contract.
public class LoadMap : IPacketSerialize
{
    public int Index;
    public int Position;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)19);

        writer.Write(EncodeIndex(Index));
        writer.Write(EncodeIndex(Position));

        return writer.Buffer;
    }

    public static byte EncodeIndex(int value) => value == -1
        ? byte.MaxValue
        : checked((byte)value);
}
