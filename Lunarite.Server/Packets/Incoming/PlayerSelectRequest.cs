using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// db
public class PlayerSelectRequest : IPacketDeserialize<PlayerSelectRequest>
{
    public int Pid;

    public bool DirectSimul;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out PlayerSelectRequest? packet)
    {
        packet = new PlayerSelectRequest();

        if (!reader.TryRead(out packet.Pid))
            return false;

        if (!reader.TryRead(out packet.DirectSimul))
            return false;

        return reader.RemainingLength == 0;
    }
}
