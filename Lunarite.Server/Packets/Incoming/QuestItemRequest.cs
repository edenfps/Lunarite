using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// `dd` body used by SendQuestInsertItem (op46), SendQuestInsertDropItem
// (op47), and SendQuestReduceItem (op50).
public sealed class QuestItemRequest : IPacketDeserialize<QuestItemRequest>
{
    public int ItemIndex;
    public int Quantity;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestItemRequest? packet)
    {
        packet = new QuestItemRequest();
        return reader.TryRead(out packet.ItemIndex)
            && reader.TryRead(out packet.Quantity)
            && reader.RemainingLength == 0;
    }
}
