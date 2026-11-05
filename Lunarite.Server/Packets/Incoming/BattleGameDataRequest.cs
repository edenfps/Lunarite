using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// Native SendBattleGameData client op152: `wwwbm`. The `m` field is raw bytes;
// each mode-specific helper emits one fixed 31-byte record per player.
public sealed record BattleGameDataRequest(
    ushort TeamScore0, ushort TeamScore1, ushort RemainingTime,
    byte BackupType, byte[] PlayerRecords) : IPacketDeserialize<BattleGameDataRequest>
{
    public int PlayerCount => PlayerRecords.Length / 31;

    public static bool TryDeserialize(ref PacketReader reader,
        [NotNullWhen(true)] out BattleGameDataRequest? packet)
    {
        packet = null;
        if (!reader.TryRead(out ushort teamScore0)
            || !reader.TryRead(out ushort teamScore1)
            || !reader.TryRead(out ushort remainingTime)
            || !reader.TryRead(out byte backupType)
            || reader.RemainingLength % 31 != 0
            || reader.RemainingLength > 64 * 31)
            return false;
        byte[] records = reader.RemainingSpan.ToArray();
        reader.Advance(records.Length);
        packet = new(teamScore0, teamScore1, remainingTime, backupType, records);
        return true;
    }
}
