using Lunarite.Network;
using Lunarite.Storage;

namespace Lunarite.Packets.Outgoing;

public sealed class TakeOffSkill : IPacketSerialize
{
    public byte Result, SkillCell, MainCell;
    public PlayerStore.GenericItem? Armor;

    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)75); w.Write(Result);
        if (Result is 0 or 32) { SkillResultWriter.WriteItem(w, Armor); w.Write(SkillCell); w.Write(MainCell); }
        return w.Buffer;
    }
}
