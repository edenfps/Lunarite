using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op236 is a compact relay-quality directory: word count followed by
// packed three-byte entries (ushort relay key, byte ping grade).
public sealed class UpdatePingTime : IPacketSerialize
{
    public List<(ushort Key, byte Grade)> Entries { get; } = [];

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)236); w.Write((ushort)Entries.Count);
        foreach (var (key, grade) in Entries) { w.Write(key); w.Write(grade); }
        return w.Buffer;
    }
}
