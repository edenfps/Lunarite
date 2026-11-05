using System;
using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// ClientTcpLink opcode 62 -> OnReceivedQuestResult. Native parser reads "bb" followed by one
// fixed eight-byte BunchOfItems block (nItemID, nQuantity) for each item.
public sealed class ReceivedQuestResult : IPacketSerialize
{
    public byte Rank;
    public List<(int ItemIndex, int Quantity)> Items = [];

    public byte[] Serialize()
    {
        if (Rank > 6)
            throw new ArgumentOutOfRangeException(nameof(Rank), Rank,
                "Huxley quest-result ranks are NONE, SS, S, A, B, C, F (0..6).");
        if (Items.Count > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Items), Items.Count, "The native item count is one byte.");

        using var writer = new PacketWriter();
        writer.Write((byte)62);
        writer.Write(Rank);
        writer.Write((byte)Items.Count);
        foreach (var item in Items)
        {
            writer.Write(item.ItemIndex);
            writer.Write(item.Quantity);
        }
        return writer.Buffer;
    }
}
