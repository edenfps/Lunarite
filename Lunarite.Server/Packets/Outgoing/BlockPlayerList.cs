using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class BlockPlayerList : IPacketSerialize
{
    public byte Result;
    public List<BlockWireEntry> Entries { get; } = [];
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)209); writer.Write(Result);
        if (Result == 0)
        {
            writer.Write((byte)Entries.Count);
            foreach (var entry in Entries)
            {
                writer.Write(entry.Pid); writer.Write(entry.Name); writer.Write(entry.Level);
                writer.Write(entry.Style); writer.Write(entry.Tribe);
            }
        }
        return writer.Buffer;
    }
}
