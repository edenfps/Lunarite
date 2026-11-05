using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class PeerAddrList : IPacketSerialize
{
    public P2PEndpoint PublicEndpoint;
    public P2PEndpoint PeerEndpoint;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)128);
        writer.Write(PublicEndpoint.Address);
        writer.Write(PublicEndpoint.Port);
        writer.Write(PeerEndpoint.Address);
        writer.Write(PeerEndpoint.Port);
        return writer.Buffer;
    }
}
