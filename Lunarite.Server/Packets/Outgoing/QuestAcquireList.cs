using Lunarite.Network;

using System.Collections.Generic;

namespace Lunarite.Packets.Outgoing;

// w
// dbbbm
public class QuestAcquireList : IPacketSerialize
{
    // Each quest: Index, RallyPoint, MaxRallyPoint, Complete, 24 bytes Objectives
    public List<AcquiredQuest> Quests = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)38);

        writer.Write((short)Quests.Count);

        foreach (var quest in Quests)
        {
            writer.Write(quest.Index);
            writer.Write(quest.RallyPoint);
            writer.Write(quest.MaxRallyPoint);
            writer.Write(quest.Complete);

            for (var i = 0; i < 8; i++)
            {
                if (i < quest.Objectives.Length)
                {
                    writer.Write(quest.Objectives[i].Count);
                    writer.Write(quest.Objectives[i].Complete);
                    writer.Write(quest.Objectives[i].Visible);
                }
                else
                {
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                }
            }
        }

        return writer.Buffer;
    }

    public struct AcquiredQuest
    {
        public int Index;
        public byte RallyPoint;
        public byte MaxRallyPoint;
        public byte Complete;
        public ObjectiveInfo[] Objectives;

        public AcquiredQuest()
        {
            Objectives = new ObjectiveInfo[8];
        }
    }

    public struct ObjectiveInfo
    {
        public byte Count;
        public byte Complete;
        public byte Visible;
    }
}
