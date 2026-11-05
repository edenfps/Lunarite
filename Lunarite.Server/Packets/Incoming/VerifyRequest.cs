using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// bdsd
public class VerifyRequest : IPacketDeserialize<VerifyRequest>
{
    public bool IsPC;

    public int UID;

    public string? ID;

    public int Unknown;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out VerifyRequest? packet)
    {
        packet = new VerifyRequest();

        if (!reader.TryRead(out packet.IsPC))
            return false;

        if (!reader.TryRead(out packet.UID))
            return false;

        if (!reader.TryRead(out packet.ID))
            return false;

        if (!reader.TryRead(out packet.Unknown))
            return false;

        return reader.RemainingLength == 0;
    }
}
