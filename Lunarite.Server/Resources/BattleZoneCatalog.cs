using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

public sealed class BattleZoneCatalog
{
    private readonly ILogger _logger;

    // HuxleySimulationCenterRule in the shipped DefaultGame.ini. Simulation rooms use
    // this list (or a per-map MaxPlayerCounts override), not BattleZoneData.MaxPlayers;
    // the latter is the separate persistent-battlefield capacity and defaults to 8.
    private static readonly HashSet<int> SimulationPlayerCounts = [2, 4, 8, 12, 16];

    public BattleZoneCatalog(ILogger<BattleZoneCatalog> logger)
    {
        _logger = logger;
    }

    // BaseSpecies is EBattleZoneSpecies: 1 Sapiens base, 2 Alternative base (never change hands),
    // 3 Sapiens, 4 Alternative (occupiable). AxisStart marks an axis's initial battleground.
    // LinkedBattleServers is (Sapiens-side neighbour, Alternative-side neighbour) by BattleServerID,
    // 0 at a chain end. A linked field is on the war front even with the default AxisID 0: the
    // UIBattleField scene has an axis panel without AxisID for the 208-209-210-213 radar chain.
    public sealed record Zone(int EntryIndex, int BattleServerId, string LevelName, byte Axis,
        int MaxPlayers, ushort GoalScore, ushort TimeLimitMinutes, bool Enabled,
        bool AxisStart = false, byte BaseSpecies = 0, int RecessMinutes = 0)
    {
        public IReadOnlyList<int> LinkedServers { get; init; } = [];

        // BattleRegisterFee: Battle Medals (609001) paid to enter the field. The entry error for a
        // shortfall is BattleMsg[17] "You have an insufficient number of battle medals."
        public int RegisterFee { get; init; }

        // array<ItemNCount> Sapi/AlterWin/LoseRewardItems: Medal of Valor 609101, Lunarite 500001,
        // Certificate of Battle 609201.
        public IReadOnlyList<(int Item, int Count)> SapiWinRewards { get; init; } = [];

        public IReadOnlyList<(int Item, int Count)> SapiLoseRewards { get; init; } = [];

        public IReadOnlyList<(int Item, int Count)> AlterWinRewards { get; init; } = [];

        public IReadOnlyList<(int Item, int Count)> AlterLoseRewards { get; init; } = [];

        // A bEnabled=false field (the Islands axis 211/214/212) cannot be hosted, so it is not on
        // the front: its axis is not listed or reservable and its maps report Safe.
        public bool OnFront => Enabled && LinkedServers.Any(x => x > 0);

        // ESpeciesType 1 Sapiens, 2 Alternative.
        public IReadOnlyList<(int Item, int Count)> RewardsFor(byte species, bool won) => species switch
        {
            1 => won ? SapiWinRewards : SapiLoseRewards,
            2 => won ? AlterWinRewards : AlterLoseRewards,
            _ => []
        };
    }

    public IReadOnlyList<Zone> All
    {
        get { EnsureLoaded(); lock (_lock) return [.. _zones.Values]; }
    }

    private readonly Dictionary<int, Zone> _zones = [];
    private readonly Dictionary<int, Zone> _serverZones = [];
    private readonly object _lock = new();
    private bool _loaded;

    public Zone? Get(int entryIndex)
    {
        EnsureLoaded();
        return _zones.GetValueOrDefault(entryIndex);
    }

    public Zone? GetByServerId(int serverId)
    {
        EnsureLoaded();
        return _serverZones.GetValueOrDefault(serverId);
    }

    // Enabled cooked entries whose level package is absent from the supplied client (106, 192).
    // Hosting one makes the client travel to a missing map, which asserts and crashes.
    private static readonly HashSet<string> MissingClientLevels =
        new(StringComparer.OrdinalIgnoreCase) { "BZT-006-Wooden", "BZT-Test-EqualityMode" };

    public bool SupportsRoom(int entryIndex, int maxPlayers)
    {
        var zone = Get(entryIndex);
        return zone is { Enabled: true } && !MissingClientLevels.Contains(zone.LevelName)
            && SimulationPlayerCounts.Contains(maxPlayers);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            string? dir = AppContext.BaseDirectory;
            string? path = null;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "data", "tables", "HuxleyBattleZoneTable.txt");
                if (File.Exists(candidate)) { path = candidate; break; }
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            if (path == null) return;
            var current = new Pending();
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
                    current = new Pending();
                else if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
                    Add(current);
                else if (line.IndexOfAny(['=', '[']) is > 0 and int split
                    && Fields.TryGetValue(line[..split], out var read))
                    read(current, line[(line.IndexOf('=') + 1)..], line);
                else if (TryParseReward(line, out string rewardList, out int rewardItem, out int rewardCount))
                {
                    if (!current.RewardItems.TryGetValue(rewardList, out var list)) current.RewardItems[rewardList] = list = [];
                    if (rewardItem > 0 && rewardCount > 0) list.Add((rewardItem, rewardCount));
                }
            }
            _logger.LogInformation("[BattleZoneCatalog] loaded {0} retail battle-zone definitions.", _zones.Count);
        }
    }

    // One BattleZoneData object while it is being read.
    private sealed class Pending
    {
        public string LevelName = "";
        // Default__HuxleyBattleZoneData: MaxPlayers 8, GoalScore 100, TimeLimit 10 minutes,
        // RecessMinutes 5; most skirmish zones omit them (analysis/HuxleyGame-script-cdos.txt).
        public int EntryIndex, BattleServerId, MaxPlayers = 8, RecessMinutes = 5, BattleRegisterFee;
        public byte AxisId, BaseBattleZoneSpecies;
        public ushort GoalScore = 100, TimeLimit = 10;
        public bool Enabled = true, AxisStart;
        public List<int> LinkedBattleServers = [];
        // Sapi/AlterWin/LoseRewardItems, keyed by array name.
        public Dictionary<string, List<(int, int)>> RewardItems = [];
    }

    private delegate void FieldReader(Pending zone, string value, string line);

    // Exported property name (the text before '=' or '[') -> how a zone reads it. The reward arrays
    // (Sapi/AlterWin/LoseRewardItems[n]) go through TryParseReward.
    private static readonly Dictionary<string, FieldReader> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BattleRegisterFee"] = (z, v, _) => int.TryParse(v, out z.BattleRegisterFee),
        ["LinkedBattleServers"] = (z, v, _) => { if (int.TryParse(v, out int link)) z.LinkedBattleServers.Add(link); },
        ["AxisStart"] = (z, v, _) => { if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) z.AxisStart = true; },
        ["RecessMinutes"] = (z, v, _) => int.TryParse(v, out z.RecessMinutes),
        ["BaseBattleZoneSpecies_Editor"] = (z, v, _) => z.BaseBattleZoneSpecies = v switch
        {
            "BZ_SPECIES_SAPIENS_BASE" => 1, "BZ_SPECIES_ALTERNATIVE_BASE" => 2,
            "BZ_SPECIES_SAPIENS" => 3, "BZ_SPECIES_ALTERNATIVE" => 4, _ => 0
        },
        ["LevelName"] = (z, _, line) => z.LevelName = Quoted(line),
        ["EntryIndex"] = (z, v, _) => int.TryParse(v, out z.EntryIndex),
        ["BattleServerID"] = (z, v, _) => int.TryParse(v, out z.BattleServerId),
        ["AxisID"] = (z, v, _) => byte.TryParse(v, out z.AxisId),
        ["MaxPlayers"] = (z, v, _) => int.TryParse(v, out z.MaxPlayers),
        ["bEnabled"] = (z, v, _) => { if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) z.Enabled = false; },
        ["GoalScore"] = (z, v, _) => ushort.TryParse(v, out z.GoalScore),
        // Minutes; the export is a float.
        ["TimeLimit"] = (z, v, _) => z.TimeLimit = (ushort)Math.Clamp((int)Math.Round(double.TryParse(v,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out double minutes) ? minutes : 0), 0, ushort.MaxValue),
    };

    private void Add(Pending pending)
    {
        if (pending.EntryIndex <= 0 || pending.LevelName.Length == 0) return;
        var zone = new Zone(pending.EntryIndex, pending.BattleServerId, pending.LevelName, pending.AxisId,
            pending.MaxPlayers, pending.GoalScore, pending.TimeLimit, pending.Enabled, pending.AxisStart,
            pending.BaseBattleZoneSpecies, pending.RecessMinutes)
        {
            LinkedServers = pending.LinkedBattleServers,
            RegisterFee = pending.BattleRegisterFee,
            SapiWinRewards = pending.RewardItems.GetValueOrDefault("SapiWinRewardItems") ?? [],
            SapiLoseRewards = pending.RewardItems.GetValueOrDefault("SapiLoseRewardItems") ?? [],
            AlterWinRewards = pending.RewardItems.GetValueOrDefault("AlterWinRewardItems") ?? [],
            AlterLoseRewards = pending.RewardItems.GetValueOrDefault("AlterLoseRewardItems") ?? []
        };
        _zones[pending.EntryIndex] = zone;
        if (pending.BattleServerId > 0) _serverZones[pending.BattleServerId] = zone;
    }

    // SapiWinRewardItems[0]=(ItemId=609101,Count=6)
    private static bool TryParseReward(string line, out string list, out int item, out int count)
    {
        list = ""; item = count = 0;
        int bracket = line.IndexOf('[');
        if (bracket <= 0 || !line.Contains("RewardItems[", StringComparison.Ordinal)) return false;
        list = line[..bracket];
        var match = Regex.Match(line, @"ItemId=(\d+),Count=(-?\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out item)
            && int.TryParse(match.Groups[2].Value, out count);
    }

    private static string Quoted(string line)
    {
        int first = line.IndexOf('"'), last = line.LastIndexOf('"');
        return first >= 0 && last > first ? line[(first + 1)..last] : "";
    }
}
