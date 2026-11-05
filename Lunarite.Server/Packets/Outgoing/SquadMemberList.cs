using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class SquadMemberList : IPacketSerialize
{
    public List<SquadMember> Members { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)100); writer.Write((byte)Members.Count);
        foreach (var member in Members)
        {
            // Native ProcSquadMemberList: `sdbbswbbbdwdwd`.
            writer.Write(member.SquadName); writer.Write(member.Pid);
            writer.Write(member.Rank); writer.Write(member.Style); writer.Write(member.Name);
            writer.Write(member.Level); writer.Write(member.IsLeader); writer.Write(member.TribeRank);
            writer.Write(member.MapIndex); writer.Write(0); writer.Write((ushort)0);
            writer.Write(0); writer.Write((ushort)0); writer.Write(member.Ping);
        }
        return writer.Buffer;
    }
}
