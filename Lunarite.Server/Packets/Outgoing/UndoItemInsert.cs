using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class UndoItemInsert : IPacketSerialize
{
    public byte Serial;
    public int ItemId;
    public ushort Count;
    public int Price;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)195); writer.Write(Serial); writer.Write(ItemId); writer.Write(Count); writer.Write(Price);
        return writer.Buffer;
    }
}
