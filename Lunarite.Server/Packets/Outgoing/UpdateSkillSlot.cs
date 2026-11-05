using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnUpdateSkillSlot reads 'b' and, on result 0 only, 'bb' (the swapped button indices).
public sealed class UpdateSkillSlot : IPacketSerialize
{
    public byte Result;
    public byte Slot1;
    public byte Slot2;

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)76); w.Write(Result);
        if (Result == 0) { w.Write(Slot1); w.Write(Slot2); }
        return w.Buffer;
    }
}
