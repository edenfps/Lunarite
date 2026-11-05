using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnQuestUpdateSkillInfo_0: d PID + b inventory + b cell.
public sealed class QuestSubItem : IPacketSerialize
{
    public int Pid;
    public byte Inventory;
    public byte Cell;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)121);
        writer.Write(Pid);
        writer.Write(Inventory);
        writer.Write(Cell);
        return writer.Buffer;
    }
}
