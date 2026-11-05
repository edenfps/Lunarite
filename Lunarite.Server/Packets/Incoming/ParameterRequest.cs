using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// bd
public class ParameterRequest : IPacketDeserialize<ParameterRequest>
{
    public byte Param;
    public int Value;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out ParameterRequest? packet)
    {
        packet = new ParameterRequest();

        if (!reader.TryRead(out packet.Param))
            return false;

        if (!reader.TryRead(out packet.Value))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString() => $"Param={Param} Value={Value}";
}
