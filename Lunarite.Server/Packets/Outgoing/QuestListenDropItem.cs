using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op59 OnQuestListenDropItem / op60 OnQuestListenDropItem_Group, the replies to
// SendQuestEnemydown and SendDropItemGroup. Both parse the same format (@0x11110720 / @0x11110870):
//   d  PID         -- pickup OwnerID; must match the receiving quest player's MMOPID.
//                     The armed enemy/factory delegate supplies the spawn location.
//   b  Count
//   Count x (d ItemIndex, d Num)
// Script path: OnQuestListenDropItem -> HuxleyItemFactory.DropItem -> Spawn(HuxleyQuestPickup).
public class QuestListenDropItem : IPacketSerialize
{
    public byte Opcode = 59;   // 59 = single (Delegate_DropItem), 60 = group
    public int Pid;
    public List<Drop> Drops = [];

    public struct Drop
    {
        public int ItemIndex;
        public int Num;

        public Drop(int itemIndex, int num)
        {
            ItemIndex = itemIndex;
            Num = num;
        }
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(Opcode);
        writer.Write(Pid);
        writer.Write((byte)Drops.Count);

        foreach (var drop in Drops)
        {
            writer.Write(drop.ItemIndex);
            writer.Write(drop.Num);
        }

        return writer.Buffer;
    }
}
