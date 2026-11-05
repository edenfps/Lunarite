using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnEquipArmorOther: dbd (PID, armor part, item id). Item id zero removes it.
public sealed class EquipArmorOther : IPacketSerialize
{
    public int Pid;
    public byte Part;
    public int ItemId;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)68); writer.Write(Pid); writer.Write(Part); writer.Write(ItemId);
        return writer.Buffer;
    }
}
