using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// d
public class SelectPlayer : IPacketSerialize
{
    public int Pid;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)15);

        writer.Write(Pid);

        return writer.Buffer;
    }
}