using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

public sealed class TransmitContentRequest : IPacketDeserialize<TransmitContentRequest>
{
    public int MessageSequence;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out TransmitContentRequest? packet)
    {
        packet = new TransmitContentRequest();
        return reader.TryRead(out packet.MessageSequence) && reader.RemainingLength == 0;
    }
}
