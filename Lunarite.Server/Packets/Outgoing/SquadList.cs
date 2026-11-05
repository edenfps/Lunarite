using Lunarite.Network;
using System.Collections.Generic;

namespace Lunarite.Packets.Outgoing;

// Native OnSquadList (server op99) parses page-direction/current-page/total-pages.
// PAGE_CUR (1) additionally carries a byte entry count, followed by `dsb` entries.
public sealed class SquadList : IPacketSerialize
{
    public readonly record struct Entry(int Key, string Name, byte MemberCount);

    public byte PageDirection;
    public byte CurrentPage;
    public byte TotalPages;
    public List<Entry> Entries { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)99); writer.Write(PageDirection); writer.Write(CurrentPage); writer.Write(TotalPages);
        if (PageDirection == 1)
        {
            writer.Write((byte)Entries.Count);
            foreach (var entry in Entries)
            {
                writer.Write(entry.Key); writer.Write(entry.Name); writer.Write(entry.MemberCount);
            }
        }
        return writer.Buffer;
    }
}
