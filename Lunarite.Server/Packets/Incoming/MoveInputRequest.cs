using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// fffw
public class MoveInputRequest : IPacketDeserialize<MoveInputRequest>
{
    public float X;
    public float Y;
    public float Z;
    public short Yaw;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out MoveInputRequest? packet)
    {
        packet = new MoveInputRequest();

        if (!reader.TryRead(out packet.X))
            return false;

        if (!reader.TryRead(out packet.Y))
            return false;

        if (!reader.TryRead(out packet.Z))
            return false;

        if (!reader.TryRead(out packet.Yaw))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString() => $"X={X:F2} Y={Y:F2} Z={Z:F2} Yaw={Yaw}";
}
