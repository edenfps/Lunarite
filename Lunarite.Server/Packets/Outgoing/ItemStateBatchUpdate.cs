using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op78 / OnUpdatedItemState: byte count followed by packed three-byte
// inventory/cell/state records. This is the batch counterpart to op214.
public sealed class ItemStateBatchUpdate : IPacketSerialize
{
    public List<ItemStateEntry> Entries { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)78);
        int count = Math.Min(byte.MaxValue, Entries.Count);
        writer.Write((byte)count);
        foreach (var entry in Entries.Take(count))
        {
            writer.Write(entry.Inventory); writer.Write(entry.Cell); writer.Write(entry.State);
        }
        return writer.Buffer;
    }
}
