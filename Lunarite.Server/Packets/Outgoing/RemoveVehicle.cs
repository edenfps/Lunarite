using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class RemoveVehicle : IPacketSerialize
{
    public byte Result;
    public byte MainCell;
    public byte RelocatedCell = 0xFF;
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)173); w.Write(Result);
        if (Result == 0) { w.Write(MainCell); w.Write(RelocatedCell); }
        return w.Buffer;
    }
}
