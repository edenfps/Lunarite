using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Native HuxleySubsystem.SendSpawn(vector, rotator): fffddd.
public sealed class SpawnRequest
{
    public float X, Y, Z;
    public int Pitch, Yaw, Roll;

    public static bool TryDeserialize(ref PacketReader reader, out SpawnRequest packet)
    {
        packet = new SpawnRequest();
        return reader.TryRead(out packet.X) && reader.TryRead(out packet.Y)
            && reader.TryRead(out packet.Z) && reader.TryRead(out packet.Pitch)
            && reader.TryRead(out packet.Yaw) && reader.TryRead(out packet.Roll)
            && reader.RemainingLength == 0;
    }
}
