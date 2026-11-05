using Lunarite.Network;

namespace Lunarite.Packets.Common;

public class GenericItemBase : ISerializableType
{
    public byte InventoryCell;

    public int ItemID;

    public int General3;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(InventoryCell);

        // Padding
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        writer.Write(ItemID);

        writer.Write(General3);
    }
}
