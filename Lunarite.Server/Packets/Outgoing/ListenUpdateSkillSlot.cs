using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnQuestUpdateSkillSlot: d PID + m[5] skill-slot permutation.
public sealed class ListenUpdateSkillSlot : IPacketSerialize
{
    public int Pid;
    public byte[] SkillSlots = [0, 1, 2, 3, 4];
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)122);
        writer.Write(Pid);
        writer.Write(SkillSlots is { Length: 5 } ? SkillSlots : [0, 1, 2, 3, 4]);
        return writer.Buffer;
    }
}
