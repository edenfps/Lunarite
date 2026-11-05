using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

public sealed class TransmitModifyRequest : IPacketDeserialize<TransmitModifyRequest>
{
    public int MessageSequence;
    public byte ModifyType;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out TransmitModifyRequest? packet)
    {
        packet = new TransmitModifyRequest();
        return reader.TryRead(out packet.MessageSequence)
            && reader.TryRead(out packet.ModifyType)
            && reader.RemainingLength == 0;
    }
}
