using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class DelPlayer : IPacketSerialize
{
    public int Pid;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)21);
        writer.Write(Pid);
        return writer.Buffer;
    }
}
