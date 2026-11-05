using Lunarite.Network;

using System.Collections.Generic;

namespace Lunarite.Packets.Outgoing;

// w
// d
public class QuestCompletionList : IPacketSerialize
{
    public List<int> Quests = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)39);

        writer.Write((short)Quests.Count);

        foreach (var quest in Quests)
            writer.Write(quest);

        return writer.Buffer;
    }
}
