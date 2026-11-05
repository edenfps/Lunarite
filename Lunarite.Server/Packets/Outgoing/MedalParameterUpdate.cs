using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op208 updates PARAMETER_MEDAL (enum value 11). Failure has only the
// leading false byte; success carries medal item ID and cumulative donated count.
public sealed class MedalParameterUpdate : IPacketSerialize
{
    public bool Success;
    public int MedalId;
    public int Count;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)208);
        writer.Write(Success);
        if (Success) { writer.Write(MedalId); writer.Write(Count); }
        return writer.Buffer;
    }
}
