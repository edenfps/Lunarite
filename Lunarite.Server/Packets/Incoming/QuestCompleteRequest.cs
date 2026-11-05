using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// db -- UHuxleySubsystem::SendQuestComplete(int nIndex, ..., int nItemSlot).
// The native client truncates the optional slot to one byte; INDEX_NONE is 0xFF.
public class QuestCompleteRequest : IPacketDeserialize<QuestCompleteRequest>
{
    public int Index;
    public byte ItemSlot;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestCompleteRequest? packet)
    {
        packet = new QuestCompleteRequest();
        return reader.TryRead(out packet.Index)
            && reader.TryRead(out packet.ItemSlot)
            && reader.RemainingLength == 0;
    }

    public override string ToString() => $"Index={Index} ItemSlot={ItemSlot}";
}
