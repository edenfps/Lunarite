using Lunarite.Network;
using Lunarite.Storage;

namespace Lunarite.Packets.Outgoing;

// Native OnQuestUpdateSkillInfo: d PID + m[32] GenericItem.
public sealed class QuestUpdateItem : IPacketSerialize
{
    public int Pid;
    public PlayerStore.GenericItem? Item;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)120);
        writer.Write(Pid);
        SkillResultWriter.WriteItem(writer, Item);
        return writer.Buffer;
    }
}
