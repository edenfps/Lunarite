using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnUpdateVehicle (server op175): observer PID plus personal-vehicle item ID.
public sealed class UpdateVehicle : IPacketSerialize
{
    public int Pid;
    public int VehicleItemId;
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)175); w.Write(Pid); w.Write(VehicleItemId); return w.Buffer;
    }
}
