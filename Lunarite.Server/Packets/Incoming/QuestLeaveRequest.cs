using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Native SendQuestLeave (sub_111C1F50) sends opcode 41 as `ww`: two unsigned
// 16-bit indices. 0xFFFF is the script's -1 return-to-faction-camp sentinel.
public sealed class QuestLeaveRequest : IPacketDeserialize<QuestLeaveRequest>
{
    public int ZoneIndex;
    public int AreaIndex;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestLeaveRequest? packet)
    {
        packet = new QuestLeaveRequest();
        if (!reader.TryRead(out ushort zone) || !reader.TryRead(out ushort area)
            || reader.RemainingLength != 0) return false;
        packet.ZoneIndex = zone == ushort.MaxValue ? -1 : zone;
        packet.AreaIndex = area == ushort.MaxValue ? -1 : area;
        return true;
    }

    public override string ToString() => $"ZoneIndex={ZoneIndex} AreaIndex={AreaIndex}";
}
