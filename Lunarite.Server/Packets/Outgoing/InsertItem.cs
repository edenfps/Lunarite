using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op82 OnInsertItem: one raw 32-byte GenericItem, copied into
// m_Item.Inventories[InvenIndex].List[Cell] (sub_11116470).
//
// GenericItem byte layout (x86 little-endian, sizeof = 32):
//   +0  int  nItemID
//   +4  int  nCount
//   +8  byte bInventoryIndex   (EINVENTORY: WEAPON=3, ARMOR=4, ...)
//   +9  byte bInventoryCell    (armor part 0-4 / weapon slot 0-2)
//   +10 byte byState
//   +11 byte (padding, struct alignment)
//   +12 int  nLimitTime        (-> ItemArmor.nSpray)
//   +16 int  nTuneUpOptions
//   +20 int  nGeneral1         (-> ItemArmor.Skill[0], socket skill)
//   +24 int  nGeneral2         (-> ItemArmor.Skill[1])
//   +28 int  nGeneral3         (-> nDurability; SetItemDurability sets this)
public class InsertItem : IPacketSerialize
{
    public int ItemID;
    public int Count = 1;

    public byte InventoryIndex;
    public byte Cell;
    public byte State;

    public int LimitTime;
    public int TuneUpOptions;

    public int Skill0;      // nGeneral1 - armor socket skill slot 0
    public int Skill1;      // nGeneral2 - armor socket skill slot 1
    public int Durability;  // nGeneral3

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)82);

        writer.Write(ItemID);
        writer.Write(Count);

        writer.Write(InventoryIndex);
        writer.Write(Cell);
        writer.Write(State);
        writer.Write((byte)0); // padding

        writer.Write(LimitTime);
        writer.Write(TuneUpOptions);
        writer.Write(Skill0);
        writer.Write(Skill1);
        writer.Write(Durability);

        return writer.Buffer;
    }
}
