using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class AddVehicle : IPacketSerialize
{
    public byte Result;
    public byte MainCell;
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)172); w.Write(Result);
        if (Result == 0) w.Write(MainCell);
        return w.Buffer;
    }
}
