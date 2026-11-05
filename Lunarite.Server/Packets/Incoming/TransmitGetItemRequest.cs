using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

public sealed class TransmitGetItemRequest : IPacketDeserialize<TransmitGetItemRequest>
{
    public int MessageSequence;
    public int ItemSequence;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out TransmitGetItemRequest? packet)
    {
        packet = new TransmitGetItemRequest();
        return reader.TryRead(out packet.MessageSequence)
            && reader.TryRead(out packet.ItemSequence)
            && reader.RemainingLength == 0;
    }
}
