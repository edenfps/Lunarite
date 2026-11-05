using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// d
public class QuestIndexRequest : IPacketDeserialize<QuestIndexRequest>
{
    public int QuestIndex;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestIndexRequest? packet)
    {
        packet = new QuestIndexRequest();

        if (!reader.TryRead(out packet.QuestIndex))
            return false;

        return reader.RemainingLength == 0;
    }

    public override string ToString() => $"QuestIndex={QuestIndex}";
}
