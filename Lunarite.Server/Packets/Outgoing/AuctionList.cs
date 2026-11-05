using System.Collections.Generic;

using Lunarite.Network;
using Lunarite.Storage;

namespace Lunarite.Packets.Outgoing;

public sealed class AuctionList : IPacketSerialize
{
    public byte Opcode;
    public byte PageDirection;
    public byte CurrentPage;
    public byte TotalPages;

    public List<AuctionStore.AuctionItemInfo> Entries { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write(Opcode); writer.Write(PageDirection); writer.Write(CurrentPage); writer.Write(TotalPages);
        if (PageDirection == 1)
        {
            writer.Write((byte)Entries.Count);
            foreach (var entry in Entries)
            {
                writer.Write(entry.Index); writer.Write(entry.SellerPid); writer.Write(entry.Item.ItemId);
                // Native OnAuctionList (op161) parses `dddddbsdb`: price precedes the
                // one-byte remain bucket; the packed tune-up value follows seller name.
                writer.Write(entry.Item.Count); writer.Write(entry.Price);
                writer.Write(AuctionStore.RemainTime(entry)); writer.Write(entry.Seller);
                // Native timeout-list op167 stops after seller (`dddddbs`). Active and
                // own-list op161/op162 append packed tune-up and reversion (`dddddbsdb`).
                if (Opcode != 167)
                {
                    writer.Write(entry.Item.TuneUpOptions);
                    writer.Write((byte)0); // Reversion
                }
            }
        }
        return writer.Buffer;
    }

}
