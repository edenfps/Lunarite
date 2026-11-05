using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class PayItem : IPacketSerialize
{
    public byte Subtype;
    public byte Result;
    public byte[] Extra = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)227);
        writer.Write(Subtype);
        writer.Write(Result);
        writer.Write(Extra);
        return writer.Buffer;
    }
}
