using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class SquadMoveInput : IPacketSerialize
{
    public int Pid;
    public byte IsLeader;
    public float X;
    public float Y;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)108);
        writer.Write(Pid);
        writer.Write(IsLeader);
        writer.Write(X);
        writer.Write(Y);
        return writer.Buffer;
    }
}
