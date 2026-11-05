using Lunarite.Network;
using Lunarite.Storage;

namespace Lunarite.Packets.Outgoing;

public sealed class EquipSkill : IPacketSerialize
{
    public byte Result, SkillCell, MainCell;
    public PlayerStore.GenericItem? Armor;

    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)74); w.Write(Result);
        if (Result == 0) { w.Write(SkillCell); w.Write(MainCell); SkillResultWriter.WriteItem(w, Armor); }
        return w.Buffer;
    }
}
