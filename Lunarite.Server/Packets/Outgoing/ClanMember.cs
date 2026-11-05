using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ClanMember : IPacketSerialize
{
    public byte Result;
    public List<ClanMemberWire> Members { get; } = [];
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)181); writer.Write(Result);
        if (Result == 13)
        {
            writer.Write((byte)Members.Count);
            foreach (var m in Members)
            {
                writer.Write(m.Pid); writer.Write(m.Name); writer.Write(m.Level); writer.Write(m.Tribe);
                writer.Write(m.Style); writer.Write(m.Grade); writer.Write(m.Login); writer.Write(m.MapState);
                writer.Write(m.MapIndex); writer.Write(m.Veteran); writer.Write(m.Ranking); writer.Write(m.MedalPoint);
            }
        }
        return writer.Buffer;
    }
}
