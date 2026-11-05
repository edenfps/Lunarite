using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// wd
public class SelectServerRequest : IPacketDeserialize<SelectServerRequest>
{
    public short Index;

    public int Unknown;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out SelectServerRequest? packet)
    {
        packet = new SelectServerRequest();

        if (!reader.TryRead(out packet.Index))
            return false;

        if (!reader.TryRead(out packet.Unknown))
            return false;

        return reader.RemainingLength == 0;
    }
}
