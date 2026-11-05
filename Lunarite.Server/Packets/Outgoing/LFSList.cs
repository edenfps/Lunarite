using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op110 OnLFSList: bbb header and PAGE_CUR count + `dsbbb` entries.
public sealed class LFSList : IPacketSerialize
{
    public byte PageDirection;
    public byte CurrentPage;
    public byte TotalPages;
    public List<LfsEntry> Entries { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)110); writer.Write(PageDirection); writer.Write(CurrentPage); writer.Write(TotalPages);
        if (PageDirection == 1)
        {
            writer.Write((byte)Entries.Count);
            foreach (var entry in Entries)
            {
                writer.Write(entry.Pid); writer.Write(entry.Name); writer.Write(entry.Level);
                writer.Write(entry.LevelClass); writer.Write(entry.Style);
            }
        }
        return writer.Buffer;
    }
}
