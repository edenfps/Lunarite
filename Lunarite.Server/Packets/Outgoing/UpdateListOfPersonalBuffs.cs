using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class UpdateListOfPersonalBuffs : IPacketSerialize
{
    public int UnixSeconds;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)232);
        writer.Write(UnixSeconds);
        return writer.Buffer;
    }
}
