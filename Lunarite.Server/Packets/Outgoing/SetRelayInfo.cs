using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class SetRelayInfo : IPacketSerialize
{
    public int HostPid;
    public P2PEndpoint RelayEndpoint;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)206);
        writer.Write(HostPid);
        writer.Write(RelayEndpoint.Address);
        writer.Write(RelayEndpoint.Port);
        return writer.Buffer;
    }
}
