using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op168 OnAuctionBidTimeout and op169 OnAuctionSold. Both always read the result byte;
// op168 reads the item ID for Success/TimeoutDeleted, while op169 reads it only for Success.
public sealed class AuctionNotification : IPacketSerialize
{
    public byte Opcode;
    public byte Result;
    public int ItemId;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write(Opcode); writer.Write(Result);
        if ((Opcode == 168 && (Result == 0 || Result == 11)) || (Opcode == 169 && Result == 0))
            writer.Write(ItemId);
        return writer.Buffer;
    }
}
