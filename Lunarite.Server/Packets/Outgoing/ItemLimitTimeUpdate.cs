using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op218 updates GenericItem.nLimitTime and refreshes its inventory presentation.
public sealed class ItemLimitTimeUpdate : IPacketSerialize
{
    public byte Inventory;
    public byte Cell;
    public int LimitTime;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)218); writer.Write(Inventory); writer.Write(Cell); writer.Write(LimitTime);
        return writer.Buffer;
    }
}
