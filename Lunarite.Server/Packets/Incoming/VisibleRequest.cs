using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// b
public class VisibleRequest : IPacketDeserialize<VisibleRequest>
{
    public bool IsVisible;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out VisibleRequest? packet)
    {
        packet = new VisibleRequest();

        if (!reader.TryRead(out packet.IsVisible))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString() => $"IsVisible={IsVisible}";
}
