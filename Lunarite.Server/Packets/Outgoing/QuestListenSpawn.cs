using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnQuestListenSpawn decodes opcode 61 as `db` (PID, answer).
public sealed class QuestListenSpawn : IPacketSerialize
{
    public int Pid;
    public byte Answer;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)61);
        writer.Write(Pid);
        writer.Write(Answer);
        return writer.Buffer;
    }
}
