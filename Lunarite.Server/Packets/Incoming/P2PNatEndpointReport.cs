using System.Diagnostics.CodeAnalysis;
using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Native sub_111D7070 sends client op155 with format bdwdw after a successful
// op127 NAT test. sub_111D4140 supplies NAT type, public IP/port, local IP/port.
public sealed class P2PNatEndpointReport : IPacketDeserialize<P2PNatEndpointReport>
{
    public byte NatType;
    public int PublicAddress;
    public ushort PublicPort;
    public int LocalAddress;
    public ushort LocalPort;

    public static bool TryDeserialize(ref PacketReader reader,
        [NotNullWhen(true)] out P2PNatEndpointReport? packet)
    {
        packet = null;
        if (!reader.TryRead(out byte natType)
            || !reader.TryRead(out int publicAddress)
            || !reader.TryRead(out ushort publicPort)
            || !reader.TryRead(out int localAddress)
            || !reader.TryRead(out ushort localPort)
            || reader.RemainingLength != 0) return false;
        packet = new P2PNatEndpointReport
        {
            NatType = natType, PublicAddress = publicAddress, PublicPort = publicPort,
            LocalAddress = localAddress, LocalPort = localPort
        };
        return true;
    }
}
