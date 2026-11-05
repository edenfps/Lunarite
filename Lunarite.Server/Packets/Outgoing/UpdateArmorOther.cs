using Lunarite.Network;
using Lunarite.Storage;

namespace Lunarite.Packets.Outgoing;

// Native OnUpdateArmorOther: dbM with a full 32-byte GenericItem.
public sealed class UpdateArmorOther : IPacketSerialize
{
    public int Pid;
    public byte Part;
    public PlayerStore.GenericItem? Item;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)80); writer.Write(Pid); writer.Write(Part);
        SkillResultWriter.WriteItem(writer, Item);
        return writer.Buffer;
    }
}
