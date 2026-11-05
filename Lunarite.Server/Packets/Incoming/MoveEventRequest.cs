using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// b
public class MoveEventRequest : IPacketDeserialize<MoveEventRequest>
{
    public byte Event;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out MoveEventRequest? packet)
    {
        packet = new MoveEventRequest();

        if (!reader.TryRead(out packet.Event))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString() => $"Event={Event}";
}
