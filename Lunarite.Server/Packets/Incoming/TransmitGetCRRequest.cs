using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

public sealed class TransmitGetCRRequest : IPacketDeserialize<TransmitGetCRRequest>
{
    public int MessageSequence;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out TransmitGetCRRequest? packet)
    {
        packet = new TransmitGetCRRequest();
        return reader.TryRead(out packet.MessageSequence) && reader.RemainingLength == 0;
    }
}
