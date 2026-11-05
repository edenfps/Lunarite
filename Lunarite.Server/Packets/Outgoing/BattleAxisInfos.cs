using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op32: count, then `bbbbbdb` per requested axis.
public sealed class BattleAxisInfos : IPacketSerialize
{
    public List<BattleAxisEntry> Entries { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)32); writer.Write((byte)Entries.Count);
        foreach (var entry in Entries)
        {
            writer.Write(entry.Axis); writer.Write(entry.Map); writer.Write(entry.Battle);
            writer.Write(entry.SapiPlayers); writer.Write(entry.AlterPlayers);
            writer.Write(entry.EstimatedWaitMinutes); writer.Write(entry.ReserveStatus);
        }
        return writer.Buffer;
    }
}
