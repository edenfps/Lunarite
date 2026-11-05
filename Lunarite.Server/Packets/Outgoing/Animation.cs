using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnAnimation: ds.
public sealed class Animation : IPacketSerialize
{
    public int Pid;
    public string Name = "";
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)26); writer.Write(Pid); writer.Write(Name);
        return writer.Buffer;
    }
}
