using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Native SendQuestInsertDropItemAtOnce -> op48, "dm": int32 count followed by count
// packed (int32 ItemIndex, int32 Num) pairs. The client uses a fixed 100-pair stack buffer.
public sealed class QuestInsertDropItemAtOnceRequest : IPacketDeserialize<QuestInsertDropItemAtOnceRequest>
{
    public List<(int ItemIndex, int Num)> Items = [];

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestInsertDropItemAtOnceRequest? packet)
    {
        packet = new QuestInsertDropItemAtOnceRequest();
        if (!reader.TryRead(out int count) || count < 0 || count > 100
            || reader.RemainingLength != count * 8)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!reader.TryRead(out int itemIndex) || !reader.TryRead(out int num))
                return false;
            packet.Items.Add((itemIndex, num));
        }
        return reader.RemainingLength == 0;
    }
}
