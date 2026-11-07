using System.Buffers.Binary;

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
        // Stored straight into sin_port, unlike the host-order ports of op124 and op128.
        writer.Write(BinaryPrimitives.ReverseEndianness(RelayEndpoint.Port));
        return writer.Buffer;
    }
}
