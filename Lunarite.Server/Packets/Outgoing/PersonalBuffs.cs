using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// Native op233 replaces the city player's complete PersonalBuff array.
public sealed class PersonalBuffs : IPacketSerialize
{
    public List<PersonalBuff> Buffs = [];
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)233);
        int count = Math.Min(byte.MaxValue, Buffs.Count);
        writer.Write((byte)count);
        foreach (var buff in Buffs.Take(count)) buff.Serialize(writer);
        return writer.Buffer;
    }
}
