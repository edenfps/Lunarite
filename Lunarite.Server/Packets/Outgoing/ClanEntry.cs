using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native ProcClanEntry (server op192): "<name> has come online" clan system message.
public sealed class ClanEntry : IPacketSerialize
{
    public string MemberName = "";
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)192); writer.Write(MemberName); return writer.Buffer;
    }
}
