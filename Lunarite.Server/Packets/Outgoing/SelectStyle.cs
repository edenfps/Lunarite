using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op216 / ProcSelectStyle:
// m[5] skill permutation, b + d[] licenses, seven inventory counts, then
// sum(counts) 32-byte GenericItem records. The client clears and rebuilds all
// inventory buckets before firing OnSelectStyle.
public sealed class SelectStyle : IPacketSerialize
{
    public sealed record Item(byte Inventory, byte[] Data);

    public byte[] SkillSlots = [0, 1, 2, 3, 4];
    public List<int> Licenses = [];
    public List<Item> Items = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)216);
        writer.Write(SkillSlots.Take(5).Concat(Enumerable.Repeat((byte)0, 5)).Take(5).ToArray());
        int licenseCount = Math.Min(byte.MaxValue, Licenses.Count);
        writer.Write((byte)licenseCount);
        foreach (int license in Licenses.Take(licenseCount)) writer.Write(license);

        var counts = new byte[7];
        foreach (var item in Items)
            if (item.Inventory < counts.Length && counts[item.Inventory] < byte.MaxValue)
                counts[item.Inventory]++;
        writer.Write(counts);
        foreach (var item in Items.Where(x => x.Inventory < counts.Length))
        {
            if (item.Data.Length != 32) throw new InvalidOperationException("GenericItem must be exactly 32 bytes.");
            writer.Write(item.Data);
        }
        return writer.Buffer;
    }
}
