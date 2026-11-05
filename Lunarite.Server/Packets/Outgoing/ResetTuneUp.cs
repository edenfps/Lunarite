using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ResetTuneUp : IPacketSerialize
{
    public byte Result, ResetType, Option;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)228); w.Write(Result);
        if (Result == 0) { w.Write(ResetType); w.Write(Option); }
        return w.Buffer;
    }
}
