using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// w
// d
public class QuestPossibleList : IPacketSerialize
{
    public List<int> Quests = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)35);

        writer.Write((short)Quests.Count);

        foreach (var quests in Quests)
            writer.Write(quests);

        return writer.Buffer;
    }
}