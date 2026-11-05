using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Native listen-client SendBattleResult branch: op114 "dbbmmmm".
public sealed record BattleResultRequest(int Pid, byte Species, byte Result,
    byte[] Durability, byte[] WeaponKills, byte[] Battle, byte[] Detail) : IPacketDeserialize<BattleResultRequest>
{
    // Native SendBattleResult @ 0x111CBE10 sends, in order: 27-byte durability block
    // ([inventory, cell, durability] for 4 WEAPON then 5 ARMOR slots), 12-byte weapon-kill block
    // ([category, kills:u16] x 4), the 48-byte battle block and the 104-byte detail block.
    public int EmbeddedPid => ReadInt(Battle, 0);
    public byte GameType => Battle.Length > 5 ? Battle[5] : (byte)0;
    public bool Won => ReadInt(Battle, 6) != 0;
    public int Kills => Math.Max(0, ReadInt(Battle, 10));
    public int AwardedLevelBp => ReadInt(Battle, 30);
    // Native log "BattleResult<1> ... Lunar: (%i)": the PRI's LunarOreAmount after EndGame's
    // AdjustDefaultReward/AdjustWinTeamReward, i.e. carried ore plus the zone's Lunarite reward.
    public int LunarOre => Math.Max(0, ReadInt(Battle, 34));
    // `*((_WORD*)PRI + 398)`: the PRI's QT (PCr) after ComputePlayerBattleResult, including the
    // PC-room/event bonus and the listen host's 3.5% bonus (native "QT : %d + HostQT : %d").
    public int QT => Battle.Length >= 48 ? BinaryPrimitives.ReadUInt16LittleEndian(Battle.AsSpan(46, 2)) : 0;

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out BattleResultRequest? packet)
    {
        packet = null;
        if (!reader.TryRead(out int pid) || !reader.TryRead(out byte species) || !reader.TryRead(out byte result)
            || !reader.TryReadExact(27, out var durability) || !reader.TryReadExact(12, out var weaponKills)
            || !reader.TryReadExact(48, out var battle) || !reader.TryReadExact(104, out var detail)
            || reader.RemainingLength != 0) return false;
        packet = new(pid, species, result, durability.ToArray(), weaponKills.ToArray(), battle.ToArray(), detail.ToArray());
        return true;
    }

    private static int ReadInt(byte[] data, int offset) => offset + 4 <= data.Length
        ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)) : 0;
}
