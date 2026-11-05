using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ClanMyInfo : IPacketSerialize
{
    public string ClanName = "";
    public byte Grade;
    public int Mark;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)185); writer.Write(ClanName);
        writer.Write(Grade); writer.Write(Mark); return writer.Buffer;
    }
}
