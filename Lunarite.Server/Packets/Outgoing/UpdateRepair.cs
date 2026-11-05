using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnUpdateRepair parses one discriminator byte. Single-item success appends the cell;
// 7 repairs all, 255 repairs equipped inventories, and 8 invokes OnUpdateRepair_Fail.
public sealed class UpdateRepair : IPacketSerialize
{
    public byte Inventory;
    public byte? Cell;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)88); writer.Write(Inventory);
        if (Cell.HasValue) writer.Write(Cell.Value);
        return writer.Buffer;
    }
}
