using System.Diagnostics.CodeAnalysis;
using System.Net;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// bbbbmb
public class ConnectRequest : IPacketDeserialize<ConnectRequest>
{
    public IPAddress? Address;

    public byte[]? Compatibility;

    public bool IsTrial;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out ConnectRequest? packet)
    {
        packet = new ConnectRequest();

        if (!reader.TryRead(out packet.Address))
            return false;

        if (!reader.TryReadExact(11, out var compatibility))
            return false;

        packet.Compatibility = new byte[compatibility.Length];

        if (!compatibility.TryCopyTo(packet.Compatibility))
            return false;

        if (!reader.TryRead(out packet.IsTrial))
            return false;

        return reader.RemainingLength == 0;
    }
}
