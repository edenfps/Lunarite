using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Incoming;

// sbm
public class PlayerCreateRequest : IPacketDeserialize<PlayerCreateRequest>
{
    public string? Name;

    public byte Index;

    public Custom Custom = new();

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out PlayerCreateRequest? packet)
    {
        packet = new PlayerCreateRequest();

        if (!reader.TryRead(out packet.Name))
            return false;

        if (!reader.TryRead(out packet.Index))
            return false;

        if (!packet.Custom.TryRead(ref reader))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString()
    {
        return $"""
               {nameof(Name)}: {Name}
               {nameof(Index)}: {Index}
               {nameof(Custom)}: ( {Custom} )
               """;
    }
}
