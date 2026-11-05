using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class UpdateItemIndex : IPacketSerialize
{
    public byte Inventory;
    public byte Cell;
    public int ItemId;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)84); writer.Write(Inventory); writer.Write(Cell); writer.Write(ItemId);
        return writer.Buffer;
    }
}
