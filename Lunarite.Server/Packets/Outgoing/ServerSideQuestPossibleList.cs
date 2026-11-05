using System;
using System.Collections.Generic;
using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnServerSideQuestPossibleList decodes opcode 36 as `w` + count * `d`.
// Unlike requested op35, this is pushed when progression changes availability.
public sealed class ServerSideQuestPossibleList : IPacketSerialize
{
    public List<int> Quests = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)36);
        int count = Math.Min(ushort.MaxValue, Quests.Count);
        writer.Write((short)count);
        for (int i = 0; i < count; i++) writer.Write(Quests[i]);
        return writer.Buffer;
    }
}
