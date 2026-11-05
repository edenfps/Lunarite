using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Packets.Incoming;

namespace Lunarite.Storage;

public sealed class BattleResultStore
{
    private readonly ILogger _logger;
    private readonly QuestStore _questStore;

    // Stored names predate the block decode: WeaponUse holds the 27-byte durability block and
    // SkillUse the 12-byte weapon-kill block (BattleResultRequest.Durability / WeaponKills).
    // Source: where the result came from. Battle[5] is EBattleZoneGameType for every source
    // (1 TDM .. 9 HM, 11 Quest), so it cannot tell a Skirmish round from a battlefield one.
    // Rows saved before Source existed read as Legacy and count as local battles. The battle
    // block's play time ([26]) arrives as 0.0 from this client, so Seconds holds the server's own
    // measure (match start or instance creation to the result) and is used when [26] is empty.
    public const byte SourceLegacy = 0, SourceLocal = 1, SourceBattlefield = 2, SourceQuest = 3;

    public sealed record Entry(DateTimeOffset RecordedAt, int Pid, byte Species, byte Result,
        byte[] WeaponUse, byte[] SkillUse, byte[] Battle, byte[] Detail,
        byte Source = SourceLegacy, string? Run = null, int Seconds = 0);

    private readonly object _lock = new();
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "battleresults.json");
    private readonly List<Entry> _entries;

    private readonly AtomicJson _atomicJson;

    public BattleResultStore(ILogger<BattleResultStore> logger, QuestStore questStore, AtomicJson atomicJson)
    {
        _logger = logger;
        _questStore = questStore;
        _atomicJson = atomicJson;
        _entries = Load();
    }

    public void Add(BattleResultRequest report, byte source = SourceLegacy, int measuredSeconds = 0)
    {
        lock (_lock)
        {
            _entries.Add(new(DateTimeOffset.UtcNow, report.Pid, report.Species, report.Result,
                report.Durability, report.WeaponKills, report.Battle, report.Detail, source,
                Seconds: Math.Max(0, measuredSeconds)));
            if (_entries.Count > 5000) _entries.RemoveRange(0, _entries.Count - 5000);
            Save();
        }
    }

    public bool TryAddUnique(BattleResultRequest report, TimeSpan replayWindow,
        byte source = SourceLocal, int measuredSeconds = 0)
    {
        lock (_lock)
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow - replayWindow;
            bool duplicate = _entries.Any(x => x.RecordedAt >= cutoff && x.Pid == report.Pid
                && x.Species == report.Species && x.Result == report.Result
                && x.WeaponUse.SequenceEqual(report.Durability)
                && x.SkillUse.SequenceEqual(report.WeaponKills)
                && x.Battle.SequenceEqual(report.Battle)
                && x.Detail.SequenceEqual(report.Detail));
            if (duplicate) return false;
            _entries.Add(new(DateTimeOffset.UtcNow, report.Pid, report.Species, report.Result,
                report.Durability, report.WeaponKills, report.Battle, report.Detail, source,
                Seconds: Math.Max(0, measuredSeconds)));
            if (_entries.Count > 5000) _entries.RemoveRange(0, _entries.Count - 5000);
            Save();
            return true;
        }
    }

    // op60 SendQuestResult carries the same 48-byte battle and 104-byte detail blocks as op114
    // (mode 11, offset 10 = enemy kills). The quest game sends it at every rally-point finish and
    // again at EndGame, each time from the same cumulative PRI, so a run keeps only its latest.
    public void SetQuestRun(int pid, string run, byte[] weaponUse, byte[] battle, byte[] detail,
        int measuredSeconds = 0)
    {
        lock (_lock)
        {
            _entries.RemoveAll(x => x.Pid == pid && x.Source == SourceQuest && x.Run == run);
            _entries.Add(new(DateTimeOffset.UtcNow, pid, 0, 0, [], weaponUse, battle, detail,
                SourceQuest, run, Math.Max(0, measuredSeconds)));
            if (_entries.Count > 5000) _entries.RemoveRange(0, _entries.Count - 5000);
            Save();
        }
    }

    public IReadOnlyList<Entry> ForPid(int pid)
    {
        lock (_lock) return _entries.Where(x => x.Pid == pid).ToList();
    }

    public void DeleteCharacter(int pid)
    {
        lock (_lock)
        {
            if (_entries.RemoveAll(x => x.Pid == pid) != 0) Save();
        }
    }

    public bool ResetRecord(int pid)
    {
        lock (_lock)
        {
            if (_entries.RemoveAll(x => x.Pid == pid) == 0) return false;
            Save();
            return true;
        }
    }

    public byte[] BuildRecordSheet(int pid, byte sheet)
    {
        var entries = ForPid(pid);
        return sheet switch
        {
            1 => BuildSummary(pid, entries),
            2 => BuildAddition(entries),
            3 => BuildModes(entries, local: true),
            4 => BuildModes(entries, local: false),
            _ => []
        };
    }

    // PvP totals for PARAMETER_TOTALKILL/TOTALDEATH: the summary's PlayerKills/BattleDeaths
    // (10, 15), which count Skirmish and Battlefield rounds but not missions.
    public (int Kills, int Deaths) TotalKillsDeaths(int pid)
    {
        var summary = BuildSummary(pid, ForPid(pid));
        return (BinaryPrimitives.ReadInt32LittleEndian(summary.AsSpan(10 * 4)),
            BinaryPrimitives.ReadInt32LittleEndian(summary.AsSpan(15 * 4)));
    }

    public byte[] BuildAnotherPlayerRecord(int pid)
    {
        var summary = BuildSummary(pid, ForPid(pid));
        int[] fields = [0, 1, 2, 3, 4, 5, 10, 15];
        var result = new byte[32];
        for (int i = 0; i < fields.Length; i++)
            summary.AsSpan(fields[i] * 4, 4).CopyTo(result.AsSpan(i * 4, 4));
        return result;
    }

    // SummaryBattleRecord as HuxleyPlayerBattleRecordMenu reads it: the preferred weapon is an
    // HWC category (1..9, 0 = none) and the preferred mode an EBattleZoneGameType (1..9, 0 = none),
    // both shown as Favorite*[value - 1]; the three Minutes_* fields are formatted as seconds;
    // AssistantPoint/TacticsPoint are the battle block's [18]/[22] sums of the five assist and
    // seven tactics detail fields; ETC kills = PlayerKills - WeaponKills, so weapon kills count PvP
    // only; the best sprees are the highest MaxSpree/MaxMultiKill tier reached, -1 for none.
    private byte[] BuildSummary(int pid, IReadOnlyList<Entry> entries)
    {
        var values = new int[19];
        var weaponKills = new int[10];
        var modeRounds = new int[10];
        var detail = new int[26];
        foreach (var entry in entries)
        {
            int mode = Mode(entry);
            bool won = ReadInt(entry.Battle, 6) != 0;
            int kills = ReadInt(entry.Battle, 10), deaths = ReadInt(entry.Battle, 14);
            int seconds = ReadFiniteSeconds(entry.Battle, 26);
            if (seconds == 0) seconds = entry.Seconds;
            values[12] += ReadInt(entry.Battle, 18);
            values[13] += ReadInt(entry.Battle, 22);
            for (int i = 0; i < detail.Length; i++) detail[i] += ReadInt(entry.Detail, i * 4);
            if (IsQuest(entry)) { values[9] += seconds; values[11] += kills; values[16] += deaths; continue; }
            if (entry.Source == SourceBattlefield)
            { values[4]++; values[5] += won ? 1 : 0; values[8] += seconds; }
            else
            { values[2]++; values[3] += won ? 1 : 0; values[7] += seconds; }
            values[10] += kills; values[15] += deaths;
            if (mode is >= 1 and <= 9) modeRounds[mode]++;
            AccumulateWeaponKills(entry.SkillUse, weaponKills);
        }
        values[0] = ArgMax(weaponKills);
        values[1] = ArgMax(modeRounds);
        values[6] = _questStore.Completed(pid).Count;
        values[14] = weaponKills.Sum();
        values[17] = HighestTier(detail, 12);
        values[18] = HighestTier(detail, 19);
        return WriteInts(values);
    }

    private byte[] BuildAddition(IReadOnlyList<Entry> entries)
    {
        var values = new int[36];
        foreach (var entry in entries)
        {
            for (int i = 0; i < 26; i++) values[i] += ReadInt(entry.Detail, i * 4);
            if (IsQuest(entry)) continue;
            var weapons = new int[10]; AccumulateWeaponKills(entry.SkillUse, weapons);
            for (int i = 0; i < 10; i++) values[26 + i] += weapons[i];
        }
        return WriteInts(values);
    }

    // LocalBattleFieldRecord is DM, BZT, BZR, BZP, CTF, HDM, HTDM; BattleFieldRecord is BZT, BZR,
    // BZP. Double-damage and Macbeth rounds have no column.
    private byte[] BuildModes(IReadOnlyList<Entry> entries, bool local)
    {
        int recordCount = local ? 7 : 3;
        var values = new int[recordCount * 3];
        foreach (var entry in entries)
        {
            if (IsQuest(entry) || (entry.Source == SourceBattlefield) == local) continue;
            int index = local ? Mode(entry) switch { 5 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 9 => 5, 8 => 6, _ => -1 }
                : Mode(entry) switch { 1 => 0, 2 => 1, 3 => 2, _ => -1 };
            if (index < 0) continue;
            values[index * 3]++;
            if (ReadInt(entry.Battle, 6) != 0) values[index * 3 + 1]++;
            values[index * 3 + 2] += ReadInt(entry.Battle, 30); // battle points
        }
        return WriteInts(values);
    }

    private static int Mode(Entry entry) => entry.Battle.Length >= 6 ? entry.Battle[5] : 0;

    private bool IsQuest(Entry entry) => entry.Source == SourceQuest || Mode(entry) == 11;

    // Index 1.. of the largest positive count (ties go to the lower index); 0 means none.
    private static int ArgMax(int[] counts)
    {
        int best = 1;
        for (int i = 2; i < counts.Length; i++)
            if (counts[i] > counts[best]) best = i;
        return counts[best] > 0 ? best : 0;
    }

    private static int HighestTier(int[] detail, int first)
    {
        for (int tier = 6; tier >= 0; tier--)
            if (detail[first + tier] > 0) return tier;
        return -1;
    }

    private static void AccumulateWeaponKills(byte[] wire, int[] totals)
    {
        for (int offset = 0; offset + 2 < wire.Length; offset += 3)
        {
            int category = wire[offset];
            if ((uint)category < totals.Length)
                totals[category] += BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(offset + 1, 2));
        }
    }

    private static int ReadInt(byte[] data, int offset) => offset + 4 <= data.Length
        ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)) : 0;

    private static int ReadFiniteSeconds(byte[] data, int offset)
    {
        float value = BitConverter.Int32BitsToSingle(ReadInt(data, offset));
        return float.IsFinite(value) && value > 0 ? (int)Math.Min(int.MaxValue, value) : 0;
    }

    private static byte[] WriteInts(int[] values)
    {
        var data = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4, 4), values[i]);
        return data;
    }

    private List<Entry> Load()
    {
        try { return _atomicJson.Read<List<Entry>>(_path) ?? []; }
        catch (Exception ex) { _logger.LogWarning("[BattleResultStore] load failed: {0}", ex.Message); return []; }
    }

    private void Save()
    {
        try { _atomicJson.Write(_path, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[BattleResultStore] save failed: {0}", ex.Message); }
    }
}
