using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server opcode 50 -> OnInsertDropItem_Quest, `bdd`.
public sealed class InsertDropItemQuest : IPacketSerialize
{
    public byte Answer;
    public int ItemIndex;
    public int Quantity;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)50);
        writer.Write(Answer);
        writer.Write(ItemIndex);
        writer.Write(Quantity);
        return writer.Buffer;
    }
}
