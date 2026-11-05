using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// b s s s s d -- SendTransmitSendSystem helper @ 0x111CADC0:
// Send(181, "bssssd", type, from, to, title, content, unique).
public class TransmitSendSystemRequest : IPacketDeserialize<TransmitSendSystemRequest>
{
    public byte TransmitType;
    public string? FromName;
    public string? ToName;
    public string? Title;
    public string? Content;
    public int Id;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out TransmitSendSystemRequest? packet)
    {
        packet = new TransmitSendSystemRequest();

        if (!reader.TryRead(out packet.TransmitType))
            return false;

        if (!reader.TryRead(out packet.FromName))
            return false;

        if (!reader.TryRead(out packet.ToName))
            return false;

        if (!reader.TryRead(out packet.Title))
            return false;

        if (!reader.TryRead(out packet.Content))
            return false;

        if (!reader.TryRead(out packet.Id))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString() => $"[{Title}] {Content}";
}
