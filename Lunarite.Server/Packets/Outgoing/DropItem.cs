using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class DropItem : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)66);
        writer.Write(Result);
        return writer.Buffer;
    }
}
