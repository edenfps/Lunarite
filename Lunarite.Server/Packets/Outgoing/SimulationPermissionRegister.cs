using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op157 uses the same MMOPermission body as quest op55 and makes the host reply on client op108.
public sealed class SimulationPermissionRegister : IPacketSerialize
{
    public SimulationPlayerState Player = new();
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)157); Player.WritePermissionBody(w, true);
        return w.Buffer;
    }
}
