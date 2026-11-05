using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server -> client, opcode 81 (OnRemoveItem). Clears the item at (InventoryIndex, Cell).
// Client parses "bb" = InventoryIndex, Cell (OnRemoveItem 0x11118fe0).
public class RemoveItem : IPacketSerialize
{
    public byte InventoryIndex;
    public byte Cell;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)81);
        writer.Write(InventoryIndex);
        writer.Write(Cell);
        return writer.Buffer;
    }
}
