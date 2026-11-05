using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnMoveInput: dfffw.
public sealed class MoveInput : IPacketSerialize
{
    public int Pid;
    public float X;
    public float Y;
    public float Z;
    public short Yaw;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)22); writer.Write(Pid);
        writer.Write(X); writer.Write(Y); writer.Write(Z); writer.Write(Yaw);
        return writer.Buffer;
    }
}
