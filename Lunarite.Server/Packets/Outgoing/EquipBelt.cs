using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op70: result, then the source MAIN cell on success.
public sealed class EquipBelt : IPacketSerialize
{
    public byte Result;
    public byte MainCell;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)70);
        writer.Write(Result);
        if (Result == 0) writer.Write(MainCell);
        return writer.Buffer;
    }
}
