using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// UDPSocketHelper::SendP2PNetworkAddresses sends client op196 as "bm":
// one count byte followed by count 12-byte (relay IP/port, my IP/port) records.
public sealed class P2PNetworkAddressesReport : IPacketDeserialize<P2PNetworkAddressesReport>
{
    public readonly record struct Entry(int RelayAddress, ushort RelayPort,
        int MyAddress, ushort MyPort);

    public List<Entry> Entries { get; } = [];

    public static bool TryDeserialize(ref PacketReader reader,
        [NotNullWhen(true)] out P2PNetworkAddressesReport? packet)
    {
        packet = null;
        if (!reader.TryRead(out byte count) || count == 0
            || reader.RemainingLength != count * 12) return false;
        var result = new P2PNetworkAddressesReport();
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryRead(out int relayAddress)
                || !reader.TryRead(out ushort relayPort)
                || !reader.TryRead(out int myAddress)
                || !reader.TryRead(out ushort myPort)) return false;
            result.Entries.Add(new Entry(relayAddress, relayPort, myAddress, myPort));
        }
        packet = result;
        return true;
    }
}
