using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

public sealed class TransmitSendRequest : IPacketDeserialize<TransmitSendRequest>
{
    public int Pid;
    public string? ToName;
    public string? Title;
    public string? Content;
    public int CR;
    public byte ItemCell1;
    public byte ItemCell2;
    public byte ItemCell3;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out TransmitSendRequest? packet)
    {
        packet = new TransmitSendRequest();
        return reader.TryRead(out packet.Pid)
            && reader.TryRead(out packet.ToName)
            && reader.TryRead(out packet.Title)
            && reader.TryRead(out packet.Content)
            && reader.TryRead(out packet.CR)
            && reader.TryRead(out packet.ItemCell1)
            && reader.TryRead(out packet.ItemCell2)
            && reader.TryRead(out packet.ItemCell3)
            && reader.RemainingLength == 0;
    }
}
