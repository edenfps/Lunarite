using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server -> client dispatch entries from g_pPacketTypeFunctions[199..205].
public sealed class TransmitList : IPacketSerialize
{
    public sealed class Entry
    {
        public int Sequence;
        public byte Type;
        public string From = "";
        public string Title = "";
        public byte Read;
    }

    public byte PageDirection;
    public byte CurrentPage;
    public byte TotalPages;
    public List<Entry> Entries = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)199);
        writer.Write(PageDirection);
        writer.Write(CurrentPage);
        writer.Write(TotalPages);
        writer.Write((byte)Entries.Count);
        foreach (var entry in Entries)
        {
            writer.Write(entry.Sequence);
            writer.Write(entry.Type);
            writer.Write(entry.From);
            writer.Write(entry.Title);
            writer.Write(entry.Read);
        }
        return writer.Buffer;
    }
}
