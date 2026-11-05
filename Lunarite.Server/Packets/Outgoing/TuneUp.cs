using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class TuneUp : IPacketSerialize
{
    public byte Result, Option;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)212); w.Write(Result);
        if (Result == 0) w.Write(Option);
        return w.Buffer;
    }
}
