using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// ss
public class LoginRequest : IPacketDeserialize<LoginRequest>
{
    public string? Id;
    public string? Pwd;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out LoginRequest? packet)
    {
        packet = new LoginRequest();

        if (!reader.TryRead(out packet.Id))
            return false;

        if (!reader.TryRead(out packet.Pwd))
            return false;

        return reader.RemainingLength == 0;
    }
}
