using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// op113: answer followed by `dsbbb` on success.
public sealed class LFSPlayerInfo : IPacketSerialize
{
    public byte Answer;
    public LfsEntry Entry;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)113); writer.Write(Answer);
        if (Answer == 0)
        {
            writer.Write(Entry.Pid); writer.Write(Entry.Name); writer.Write(Entry.Level);
            writer.Write(Entry.LevelClass); writer.Write(Entry.Style);
        }
        return writer.Buffer;
    }
}
