using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class AuctionResult : IPacketSerialize
{
    public byte Opcode;
    public byte Answer;
    public byte? Cell;
    public int? ItemIndex;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write(Opcode); writer.Write(Answer);
        if (Answer == 0 && Cell.HasValue) writer.Write(Cell.Value);
        if (Answer == 0 && ItemIndex.HasValue) writer.Write(ItemIndex.Value);
        return writer.Buffer;
    }
}
