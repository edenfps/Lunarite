using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Client dispatcher op51 -> OnInsertDropItem_AtOnce_Quest. Native parser reads "bd", then one
// fixed eight-byte memory block per item and splits each block into IndexList/NumList.
public sealed class InsertDropItemAtOnceQuest : IPacketSerialize
{
    public byte Answer;
    public List<(int ItemIndex, int Num)> Items = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)51);
        writer.Write(Answer);
        writer.Write(Items.Count);
        foreach (var item in Items)
        {
            writer.Write(item.ItemIndex);
            writer.Write(item.Num);
        }
        return writer.Buffer;
    }
}
