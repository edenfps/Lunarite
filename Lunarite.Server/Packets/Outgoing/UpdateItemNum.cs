using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnUpdateItemNum / OnUpdateItemIndex both decode `bbd`.
public sealed class UpdateItemNum : IPacketSerialize
{
    public byte Inventory;
    public byte Cell;
    public int Count;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)83); writer.Write(Inventory); writer.Write(Cell); writer.Write(Count);
        return writer.Buffer;
    }
}
