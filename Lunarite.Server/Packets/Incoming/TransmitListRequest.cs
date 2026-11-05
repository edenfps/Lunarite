using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Client -> server mail requests, from the native SendTransmit* helpers.
public sealed class TransmitListRequest : IPacketDeserialize<TransmitListRequest>
{
    public byte PageDirection;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out TransmitListRequest? packet)
    {
        packet = new TransmitListRequest();
        return reader.TryRead(out packet.PageDirection) && reader.RemainingLength == 0;
    }
}
