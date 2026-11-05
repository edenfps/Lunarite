using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnQuestShareRequest decodes opcode 47 as `dd`.
public sealed class QuestShareRequest : IPacketSerialize
{
    public int SharerPid;
    public int QuestIndex;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)47);
        writer.Write(SharerPid);
        writer.Write(QuestIndex);
        return writer.Buffer;
    }
}
