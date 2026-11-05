using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op33: count, then nine bytes per requested battlefield.
public sealed class BattleFieldInfos : IPacketSerialize
{
    public List<BattleFieldEntry> Entries { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)33); writer.Write((byte)Entries.Count);
        foreach (var entry in Entries)
        {
            writer.Write(entry.Map); writer.Write(entry.Status); writer.Write(entry.SapiOccupy);
            writer.Write(entry.SapiAttack); writer.Write(entry.AlterAttack); writer.Write(entry.BattleTime);
            writer.Write(entry.NextBattleTime); writer.Write(entry.SapiPlayers); writer.Write(entry.AlterPlayers);
        }
        return writer.Buffer;
    }
}
