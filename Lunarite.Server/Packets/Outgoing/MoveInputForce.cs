using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op23 invokes OnMoveInputForce(float X, float Y, float Z, word Yaw).
public sealed class MoveInputForce : IPacketSerialize
{
    public float X;
    public float Y;
    public float Z;
    public short Yaw;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)23);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Z);
        writer.Write(Yaw);
        return writer.Buffer;
    }
}
