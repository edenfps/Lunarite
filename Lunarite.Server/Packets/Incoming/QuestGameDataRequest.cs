using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// UHuxleySubsystem::SendQuestGameData sends one "bbdwbm" record per controller.
// ItemObject is exactly two int32 fields (ID, Quantity) in the exported script.
public sealed class QuestGameDataRequest : IPacketDeserialize<QuestGameDataRequest>
{
    public readonly record struct StockItem(int Id, int Quantity);

    public byte Index;
    public byte Total;
    public int Pid;
    public ushort EncodedLevelBp;
    public List<StockItem> Items = [];

    public static bool TryDeserialize(ref PacketReader reader,
        [NotNullWhen(true)] out QuestGameDataRequest? packet)
    {
        packet = null;
        if (!reader.TryRead(out byte index) || !reader.TryRead(out byte total)
            || !reader.TryRead(out int pid) || !reader.TryRead(out ushort levelBp)
            || !reader.TryRead(out byte itemCount) || total == 0 || index >= total
            || reader.RemainingLength != itemCount * 8)
            return false;

        var items = new List<StockItem>(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            if (!reader.TryRead(out int id) || !reader.TryRead(out int quantity))
                return false;
            items.Add(new StockItem(id, quantity));
        }
        packet = new QuestGameDataRequest
        {
            Index = index, Total = total, Pid = pid,
            EncodedLevelBp = levelBp, Items = items
        };
        return true;
    }
}
