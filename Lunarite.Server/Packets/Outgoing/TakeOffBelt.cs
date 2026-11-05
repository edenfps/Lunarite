using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op73: result, then resolved MAIN destination and optional MAIN relocation.
public sealed class TakeOffBelt : IPacketSerialize
{
    public byte Result;
    public byte MainCell;
    public byte RelocatedMainCell = 0xFF;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)73);
        writer.Write(Result);
        if (Result == 0)
        {
            writer.Write(MainCell);
            writer.Write(RelocatedMainCell);
        }
        return writer.Buffer;
    }
}
