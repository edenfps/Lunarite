using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native ServerVersion decodes opcode 207 as `d` and prints [SYSTEM] ServerVersion(%d).
public sealed class ServerVersion : IPacketSerialize
{
    public int Version;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)207);
        writer.Write(Version);
        return writer.Buffer;
    }
}
