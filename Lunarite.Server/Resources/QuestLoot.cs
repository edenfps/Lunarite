using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

// Rolls quest drops from data/HuxleyQuestItemDropGroupData.txt. An enemy maps to drop groups;
// a group has DropItems (rolled independently) and SpecialDropItems (up to SpecialDropCount).
// Entries are (ItemIndex, Quanity, probability out of 1,000,000, DifficultyLevel). The master
// rolls on SendQuestEnemydown (op56, which carries the enemy EntryIndex) and replies op59.
public sealed class QuestLoot
{
    private readonly ILogger _logger;
    private readonly ItemCatalog _itemCatalog;

    public sealed record ReferenceAudit(
        int Enemies,
        int Groups,
        int EnemyGroupReferences,
        IReadOnlyList<int> MissingGroups,
        IReadOnlyList<int> InvalidGroups,
        IReadOnlyList<int> MissingReachableItems,
        IReadOnlyList<int> MissingUnreferencedItems);

    public sealed record Drop(int ItemIndex, int Quantity, double Probability, int DifficultyLevel);

    public sealed class Group
    {
        public readonly List<Drop> DropItems = [];
        public int SpecialDropCount;
        public readonly List<Drop> SpecialDropItems = [];
    }

    private const double ProbScale = 1_000_000.0;

    // Color sprays from bosses (user, 2026-11-11). Retail drops only Red (Coldblooded Romeo) and Blue (one level 30
    // named enemy); the other 21 colors had no source at all. Each boss here also rolls BossSprayChance for one color of
    // its level band, on top of its own drop groups (several bosses share group 80099, so the groups stay as they are).
    // Softer colors at low levels, darker ones late (docs/DYE.md).
    public const double BossSprayChance = 80_000; // 8 %, as retail's Red
    private static readonly int[] SprayBand11 = [664003, 664004, 664006, 664007];   // Yellow, Green, White, Black
    private static readonly int[] SprayBand15 = [664008, 664009, 664014];           // Pink, Dark Red, Light Yellow
    private static readonly int[] SprayBand19 = [664005, 664010];                   // Fuchsia, E. Pink
    private static readonly int[] SprayBand21 = [664011, 664012];                   // Emerald Green, Fluorescent Blue
    private static readonly int[] SprayBand23 = [664013, 664015, 664016];           // Canary, Amber, Lime
    private static readonly int[] SprayBand26 = [664017, 664018];                   // Khaki, Deep Pink
    private static readonly int[] SprayBand28 = [664019, 664021, 664002];           // Blue Violet, Dark Blue, Blue
    private static readonly int[] SprayBand30 = [664020, 664022, 664023];           // Golden Olive, Dark Purple, Dark Wine
    public static readonly IReadOnlyDictionary<int, int[]> BossSprays = new Dictionary<int, int[]>
    {
        // L11 Spitfire Othello, Baneful Brabantio
        [9021118] = SprayBand11, [9021119] = SprayBand11, [9021128] = SprayBand11, [9021129] = SprayBand11,
        // L15-16 Jackal, Platoon Leader Balta, Platoon Commander Solan
        [9191549] = SprayBand15, [9111690] = SprayBand15, [9111691] = SprayBand15,
        // L19 Romeo (Coldblooded Romeo keeps its table Red)
        [9011010] = SprayBand19,
        // L21 Macbeth Guf, Knighted Romeo
        [9032129] = SprayBand21, [9012121] = SprayBand21,
        // L23-25 Nightmare King, Firestorm Romeo
        [9052310] = SprayBand23, [9052311] = SprayBand23, [9062518] = SprayBand23, [9062519] = SprayBand23,
        [9012510] = SprayBand23, [9012511] = SprayBand23,
        // L26-27 Invincible / Bloodthirsty Panthino, Hermit Grantino
        [9042610] = SprayBand26, [9042611] = SprayBand26, [9042720] = SprayBand26,
        // L28-29 Shadowed Othello / Brabantio, Romeo Core
        [9022819] = SprayBand28, [9022829] = SprayBand28, [9012910] = SprayBand28, [9012920] = SprayBand28,
        // L30-32 Romeo the Angler / the Swindler, Desert Romeo, Macbeth Feng
        [9013099] = SprayBand30, [9013098] = SprayBand30, [9013097] = SprayBand30, [9013010] = SprayBand30,
        [9033259] = SprayBand30,
    };
    // Groups used by HuxleySeqAct_SpawnItem in the US-client cooked .war packages (68 packages):
    // 19 literal GroupIndex properties, 16 reached through SeqVar_Int, SeqVar_Named (across
    // sublevels) or a prefab SeqVar_External. Includes Deep Blue treasure/Tarconium Red
    // 82021/82022, Pluto ores 83028/83029 and Drava mission reports 83143-83151.
    // HuxleyItemFactory.RequestItem is the only exported script caller of op57.
    private static readonly HashSet<int> ScriptedGroupIndices =
    [
        80107, 80108, 81136, 81137, 82161, 82162, 82163, 82164, 82165,
        82166, 82167, 82168, 82169, 83101, 83127, 83128, 83129, 83130, 83232,
        82020, 82021, 82022, 83028, 83029, 83143, 83144, 83145, 83146, 83147,
        83148, 83149, 83150, 83151, 83341, 83342
    ];
    private readonly Dictionary<int, Group> _groups = new();
    // Enemy EntryIndex (== op56 Id == numeric EnemyName) -> its ItemDropGroupIndex list.
    private readonly Dictionary<int, List<int>> _enemyGroups = new();
    // Enemy EntryIndex -> its table XP, the base kill BP (HuxleyEnemyCharacter.XP = EnemyData.XP).
    private readonly Dictionary<int, int> _enemyXp = new();
    // Enemy EntryIndex -> its table EnemyLevel (HuxleyQuestEnemyData.EnemyLevel).
    private readonly Dictionary<int, int> _enemyLevels = new();
    // itemIndex -> required Level (data/HuxleyItemLevels.txt); absent items have no requirement.
    private readonly Dictionary<int, int> _itemLevels = new();
    // QUEST_MAX_ITEM_LEVEL excludes drops above that level; int.MaxValue disables the filter.
    public int MaxItemLevel =
        int.TryParse(Environment.GetEnvironmentVariable("QUEST_MAX_ITEM_LEVEL"), out var ml) ? ml : int.MaxValue;
    private readonly object _loadLock = new();
    private volatile bool _loaded;

    private readonly CraftDrops _craftDrops;

    public QuestLoot(ILogger<QuestLoot> logger, ItemCatalog itemCatalog, CraftDrops craftDrops)
    {
        _logger = logger;
        _itemCatalog = itemCatalog;
        _craftDrops = craftDrops;
    }

    // Random.Shared: thread-safe and seeded per process, so restarts do not replay a loot sequence.
    private bool ProbabilityHit(double probability) =>
        probability >= ProbScale || Random.Shared.NextDouble() * ProbScale < probability;

    private readonly Regex _dropRx = new(
        @"(?:DropItem|SpecialDropItem)\[\d+\]=\(ItemIndex=(\d+),Quanity=(\d+),probability=([\d.]+),DifficultyLevel=(\d+)\)",
        RegexOptions.Compiled);

    // Locate a data table file: prefer alongside the exe, else walk up to the repo /data.
    private static string? FindTable(string name)
    {
        var dirs = new List<string>();
        var directory = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && directory != null; i++)
        {
            dirs.Add(directory);
            dirs.Add(Path.Combine(directory, "data"));
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }
        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    // Enemy table: EntryIndex (== op56 Id) -> its ItemDropGroupIndex[] list.
    private void LoadEnemies()
    {
        var path = FindTable("HuxleyQuestEnemyData.txt");
        if (path == null) { _logger.LogWarning("[Loot] enemy table not found; per-enemy loot unavailable."); return; }
        try
        {
            int entryIndex = 0, xp = 0, level = 0; var groups = new List<int>();
            void Flush()
            {
                if (entryIndex != 0 && groups.Count > 0) _enemyGroups[entryIndex] = new List<int>(groups);
                if (entryIndex != 0 && xp > 0) _enemyXp[entryIndex] = xp;
                if (entryIndex != 0 && level > 0) _enemyLevels[entryIndex] = level;
                entryIndex = 0;
                xp = 0;
                level = 0;
                groups.Clear();
            }
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("begin object") && line.Contains("HuxleyQuestEnemyData")) { Flush(); continue; }
                if (line.StartsWith("EntryIndex=")) int.TryParse(line["EntryIndex=".Length..], out entryIndex);
                else if (line.StartsWith("XP=")) int.TryParse(line["XP=".Length..], out xp);
                else if (line.StartsWith("EnemyLevel=")) int.TryParse(line["EnemyLevel=".Length..], out level);
                else if (line.StartsWith("ItemDropGroupIndex["))
                {
                    int separator = line.IndexOf('=');
                    if (separator > 0 && int.TryParse(line[(separator + 1)..], out var groupIndex) && groupIndex != 0)
                        groups.Add(groupIndex);
                }
            }
            Flush();
            _logger.LogInformation("[Loot] loaded {0} enemies (Id->drop group).", _enemyGroups.Count);
        }
        catch (Exception ex) { _logger.LogWarning("[Loot] failed to load enemy table: {0}", ex.Message); }
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            LoadEnemies();
            LoadItemLevels();
            LoadDropGroups();
            _loaded = true;
        }
    }

    // Drop-group table: per-table EntryIndex, offset by that table's MasterIndex -> Group.
    private void LoadDropGroups()
    {
        var path = FindTable("HuxleyQuestItemDropGroupData.txt");
        if (path == null)
        {
            _logger.LogWarning("[Loot] drop table not found; rolls will be empty.");
            return;
        }
        try
        {
            ParseDropGroups(path);
        }
        catch (Exception ex) { _logger.LogWarning("[Loot] failed to load drop table: {0}", ex.Message); }
        _logger.LogInformation("[Loot] loaded {0} drop groups from {1}", _groups.Count, path);
    }

    private void ParseDropGroups(string path)
    {
        Group? group = null;
        bool inSpecial = false;
        var tableOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        string? currentTable = null;
        string? groupTable = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("// Reference: HuxleyQuestItemDropGroupData'", StringComparison.Ordinal))
            {
                const string tableMarker = "HuxleyQuestItemDropGroupTable.";
                int start = line.IndexOf(tableMarker, StringComparison.Ordinal);
                if (start >= 0) groupTable = SliceUntil(line, start + tableMarker.Length, '.');
                continue;
            }
            if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase)
                && line.Contains("class=HuxleyGame.HuxleyDataTable", StringComparison.Ordinal))
            {
                int start = line.IndexOf("name=\"", StringComparison.Ordinal);
                if (start >= 0) currentTable = SliceUntil(line, start + "name=\"".Length, '"');
                continue;
            }
            if (currentTable != null)
            {
                if (line.StartsWith("MasterIndex=", StringComparison.Ordinal)
                    && int.TryParse(line["MasterIndex=".Length..], out int offset))
                    tableOffsets[currentTable] = offset;
                if (line.Equals("end object", StringComparison.OrdinalIgnoreCase)) currentTable = null;
                continue;
            }
            if (line.StartsWith("begin object") && line.Contains("HuxleyQuestItemDropGroupData"))
            { group = new Group(); inSpecial = false; continue; }
            if (group == null) continue;
            if (line.StartsWith("SpecialDropCount="))
            {
                inSpecial = true;
                int.TryParse(line["SpecialDropCount=".Length..], out group.SpecialDropCount);
                continue;
            }
            var match = _dropRx.Match(line);
            if (match.Success)
            {
                if (TryCreateDrop(match, out var drop))
                    (line.StartsWith("SpecialDropItem") || inSpecial && line.StartsWith("SpecialDropItem")
                        ? group.SpecialDropItems : group.DropItems).Add(drop);
                continue;
            }
            if (line.StartsWith("EntryIndex="))
            {
                if (int.TryParse(line["EntryIndex=".Length..], out var entryIndex))
                    _groups[checked(entryIndex + (groupTable != null
                        ? tableOffsets.GetValueOrDefault(groupTable) : 0))] = group;
                group = null;
            }
        }
    }

    // The text from start up to the terminator, or null when it is empty or unterminated.
    private static string? SliceUntil(string line, int start, char terminator)
    {
        int end = line.IndexOf(terminator, start);
        return end > start ? line[start..end] : null;
    }

    private static bool TryCreateDrop(Match match, [NotNullWhen(true)] out Drop? drop)
    {
        drop = null;
        int itemIndex = int.Parse(match.Groups[1].Value);
        int quantity = int.Parse(match.Groups[2].Value);
        double probability = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        // Some retail groups have an all-zero padding row. Probabilities above
        // 1,000,000 are guaranteed drops.
        if (itemIndex == 0 || quantity <= 0 || probability <= 0) return false;
        drop = new Drop(
            itemIndex,
            quantity,
            probability,
            int.Parse(match.Groups[4].Value));
        return true;
    }

    // itemIndex -> required Level, from data/HuxleyItemLevels.txt ("<index> <level>" per line).
    private void LoadItemLevels()
    {
        var path = FindTable("HuxleyItemLevels.txt");
        if (path == null) return;   // no level filtering
        try
        {
            foreach (var raw in File.ReadLines(path))
            {
                var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[0], out var itemIndex)
                    && int.TryParse(parts[1], out var level) && level > 0)
                    _itemLevels[itemIndex] = level;
            }
            _logger.LogInformation("[Loot] loaded {0} item levels (max drop level = {1}).", _itemLevels.Count,
                MaxItemLevel == int.MaxValue ? "unfiltered" : MaxItemLevel.ToString());
        }
        catch (Exception ex) { _logger.LogWarning("[Loot] item-level load failed: {0}", ex.Message); }
    }

    // Items without a level requirement are always allowed.
    private bool LevelOk(int itemIndex) =>
        MaxItemLevel == int.MaxValue || !_itemLevels.TryGetValue(itemIndex, out var level) || level <= MaxItemLevel;

    public bool HasGroup(int groupIndex) { EnsureLoaded(); return _groups.ContainsKey(groupIndex); }

    public static bool IsScriptedGroup(int groupIndex) => ScriptedGroupIndices.Contains(groupIndex);

    public static int ScriptedGroupCount => ScriptedGroupIndices.Count;

    // Consistency check of the extracted tables. A missing enemy->group link would turn kills
    // into empty op59 replies.
    public ReferenceAudit AuditReferences()
    {
        EnsureLoaded();
        var references = _enemyGroups.Values.SelectMany(x => x).ToList();
        var missing = references.Where(x => !_groups.ContainsKey(x)).Distinct().OrderBy(x => x).ToList();
        var invalid = _groups
            .Where(pair => pair.Value.SpecialDropCount < 0
                || pair.Value.DropItems.Concat(pair.Value.SpecialDropItems).Any(drop =>
                    drop.ItemIndex <= 0 || drop.Quantity <= 0 || drop.Probability < 0
                    || drop.DifficultyLevel < 0))
            .Select(pair => pair.Key).OrderBy(x => x).ToList();
        var reachableGroups = references.Concat(ScriptedGroupIndices).ToHashSet();
        var missingReachableItems = _groups
            .Where(pair => reachableGroups.Contains(pair.Key))
            .SelectMany(pair => pair.Value.DropItems.Concat(pair.Value.SpecialDropItems))
            .Select(drop => drop.ItemIndex).Distinct()
            .Where(itemId => _itemCatalog.Get(itemId) == null)
            .Order().ToList();
        var missingUnreferencedItems = _groups
            .Where(pair => !reachableGroups.Contains(pair.Key))
            .SelectMany(pair => pair.Value.DropItems.Concat(pair.Value.SpecialDropItems))
            .Select(drop => drop.ItemIndex).Distinct()
            .Where(itemId => _itemCatalog.Get(itemId) == null)
            .Order().ToList();
        return new ReferenceAudit(_enemyGroups.Count, _groups.Count, references.Count,
            missing, invalid, missingReachableItems, missingUnreferencedItems);
    }

    public bool EnemyCanDrop(int enemyId, int itemIndex, int difficulty)
    {
        EnsureLoaded();
        if (BossSprays.TryGetValue(enemyId, out var sprays) && sprays.Contains(itemIndex)) return true;
        if (_craftDrops.CanDrop(_enemyLevels.GetValueOrDefault(enemyId), itemIndex)) return true;
        return _enemyGroups.TryGetValue(enemyId, out var groups)
            && groups.Any(groupIndex => _groups.TryGetValue(groupIndex, out var group)
                && group.DropItems.Concat(group.SpecialDropItems)
                    .Any(drop => drop.ItemIndex == itemIndex && drop.DifficultyLevel <= difficulty));
    }

    // Table XP of an enemy (0 when absent): the base BP its kill gives (HuxleyQuestGame.Killed).
    public int EnemyXp(int enemyId)
    {
        EnsureLoaded();
        return _enemyXp.GetValueOrDefault(enemyId);
    }

    // Table EnemyLevel of an enemy (0 when absent or unset: 8 of the 936 rows, none above 121 XP).
    public int EnemyLevel(int enemyId)
    {
        EnsureLoaded();
        return _enemyLevels.GetValueOrDefault(enemyId);
    }

    // op56 Id (enemy EntryIndex) -> its ItemDropGroupIndex list, each rolled. Null for an unknown enemy.
    public List<(int item, int num)>? RollForEnemy(int enemyId, int difficulty)
    {
        EnsureLoaded();
        if (!_enemyGroups.TryGetValue(enemyId, out var groupIndices)) return null;
        var result = new List<(int, int)>();
        foreach (var groupIndex in groupIndices) result.AddRange(Roll(groupIndex, difficulty));
        if (BossSprays.TryGetValue(enemyId, out var sprays) && ProbabilityHit(BossSprayChance))
            result.Add((sprays[Random.Shared.Next(sprays.Length)], 1));
        result.AddRange(_craftDrops.Roll(_enemyLevels.GetValueOrDefault(enemyId), ProbabilityHit));
        return result;
    }

    // DropItems roll independently. SpecialDropItems also roll independently, and SpecialDropCount
    // of the hits are kept, chosen at random. The client never rolls drops (no script or native
    // reader of SpecialDropCount), so the retail rule is unknown; keeping the first hits in list
    // order would make equal entries unequal (group 80007: six 33% items, the first won 33% of
    // rolls and the sixth 4%). Entries above the quest difficulty are skipped.
    public List<(int item, int num)> Roll(int groupIndex, int difficulty)
    {
        EnsureLoaded();
        var result = new List<(int, int)>();
        if (!_groups.TryGetValue(groupIndex, out var group)) return result;

        foreach (var drop in group.DropItems)
        {
            if (drop.DifficultyLevel > difficulty || !LevelOk(drop.ItemIndex)) continue;
            if (ProbabilityHit(drop.Probability)) result.Add((drop.ItemIndex, drop.Quantity));
        }
        if (group.SpecialDropCount > 0 && group.SpecialDropItems.Count > 0)
        {
            var hits = group.SpecialDropItems
                .Where(drop => drop.DifficultyLevel <= difficulty && LevelOk(drop.ItemIndex)
                    && ProbabilityHit(drop.Probability))
                .ToArray();
            Random.Shared.Shuffle(hits);
            foreach (var drop in hits.Take(group.SpecialDropCount)) result.Add((drop.ItemIndex, drop.Quantity));
        }
        return result;
    }
}
