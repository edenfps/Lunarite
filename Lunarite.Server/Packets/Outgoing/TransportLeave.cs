using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnTransportLeave, server op119: `bfffw`.
public sealed class TransportLeave : IPacketSerialize
{
    public byte Answer;
    public float X;
    public float Y;
    public float Z;
    public short Yaw;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)119); writer.Write(Answer);
        writer.Write(X); writer.Write(Y); writer.Write(Z); writer.Write(Yaw);
        return writer.Buffer;
    }
}
