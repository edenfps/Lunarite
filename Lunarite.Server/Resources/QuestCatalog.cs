using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

// Authoritative quest metadata loaded from QuestExtractor's HuxleyQuestData dump. In particular,
// EntryIndex is the wire quest id while Level is the UE3 package the quest host must START; those
// names are not universally derivable from the id (810101 -> Q-SP_Begin), and city/free quests can
// intentionally have no Level at all.
public sealed class QuestCatalog
{
    private readonly ILogger _logger;

    public sealed record AuditResult(int QuestObjects, int RewardStages, int NonPrimaryRewardStages,
        int ResultDropQuests, int ExtractionErrors);

    public sealed record ItemRequirement(int ItemIndex, int ItemCount);

    public sealed record ObjectiveDefinition(int Index, int CompleteCount,
        string ObjectiveType = "QuestObjectiveType_Primary", string BattleZoneGameType = "",
        string BattleZoneObjective = "", int ExtraParam = 0, int ExtraParam2 = 0);

    public sealed record Quest(int EntryIndex, string QuestName, string? Level, string QuestType,
        string QuestSubType, string ShareType, byte MinPlayers, byte MaxPlayers, bool Repeatable,
        IReadOnlyList<ItemRequirement> EnterRequestItems,
        IReadOnlyList<int> QuestItems, IReadOnlyList<int> RemoveItems,
        IReadOnlyList<ObjectiveDefinition> Objectives, int ExitZoneIndex = -1, int ExitAreaIndex = -1,
        bool UseContamination = false, int ContaminationAmount = 0, int RecommendLevel = 0,
        float RecommendLevelFactor = 0, int MaxLife = 0, bool UseQuestRoom = false,
        int DurabilityDecParam = 0, bool ResultDrop = false, int DropGradeS = 0,
        int DropGradeA = 0, int DropGradeB = 0, int DropGradeC = 0,
        IReadOnlyList<int>? ResultTimeLimits = null);

    private static readonly Regex RequestItemRx = new(
        @"^EnterRequestItems\[\d+\]=\(ItemIndex=(?<item>\d+),ItemCount=(?<count>\d+)\)$",
        RegexOptions.Compiled);
    private static readonly Regex ObjectiveRx = new(
        @"^Objectives\[(?<index>\d+)\]=\(.*ObjectiveCompleteCount=(?<count>\d+),",
        RegexOptions.Compiled);

    private readonly object _lock = new();
    private readonly Dictionary<int, Quest> _quests = [];
    private HashSet<string> _clientMaps = new(StringComparer.OrdinalIgnoreCase);
    private AuditResult _audit = new(0, 0, 0, 0, 0);
    private bool _loaded;

    public QuestCatalog(ILogger<QuestCatalog> logger)
    {
        _logger = logger;
    }

    public Quest? Get(int entryIndex)
    {
        EnsureLoaded();
        return _quests.GetValueOrDefault(entryIndex);
    }

    public IReadOnlyCollection<Quest> All
    {
        get { EnsureLoaded(); return _quests.Values; }
    }

    public AuditResult Audit()
    {
        EnsureLoaded();
        return _audit;
    }

    public bool ClientHasMap(string packageName)
    {
        EnsureLoaded();
        // If the configured client is unavailable (e.g. a dedicated deployment), metadata remains
        // usable and the server warns during load rather than rejecting every quest.
        return _clientMaps.Count == 0 || _clientMaps.Contains(packageName);
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            LoadQuests();
            LoadClientMaps();
        }
    }

    private static string? FindDump()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 7 && dir != null; i++)
        {
            string direct = Path.Combine(dir, "HuxleyQuestData.txt");
            string nested = Path.Combine(dir, "data", "HuxleyQuestData.txt");
            if (File.Exists(direct)) return direct;
            if (File.Exists(nested)) return nested;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    private void LoadQuests()
    {
        string? path = FindDump();
        if (path == null)
        {
            _logger.LogError("[QuestCatalog] HuxleyQuestData.txt not found; quest hosting is disabled.");
            return;
        }

        try
        {
            bool inQuest = false;
            int depth = 0;
            var quest = new Pending();
            int questObjects = 0, rewardStages = 0, nonPrimaryRewardStages = 0, extractionErrors = 0;

            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
                {
                    if (!inQuest && line.Contains("class=HuxleyGame.HuxleyQuestData", StringComparison.OrdinalIgnoreCase))
                    {
                        questObjects++;
                        inQuest = true; depth = 1;
                        quest = new Pending();
                    }
                    else if (inQuest) depth++;
                    continue;
                }
                if (!inQuest) continue;
                if (line.Contains("/* ERROR:", StringComparison.Ordinal)) extractionErrors++;
                if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
                {
                    if (--depth == 0)
                    {
                        if (quest.EntryIndex != 0) _quests[quest.EntryIndex] = quest.Build();
                        inQuest = false;
                    }
                    continue;
                }
                if (depth != 1) continue; // ignore nested condition/object EntryIndex fields

                int split = line.IndexOfAny(['=', '[']);
                if (split > 0 && Fields.TryGetValue(line[..split], out var read)
                    && read(quest, line[(line.IndexOf('=') + 1)..], line))
                    continue;
                if (line.StartsWith("Transactions[", StringComparison.Ordinal))
                {
                    rewardStages++;
                    if (!line.Contains("Name=\"PO_1\"", StringComparison.Ordinal)) nonPrimaryRewardStages++;
                }
                else
                {
                    var objective = ObjectiveRx.Match(line);
                    if (objective.Success
                        && int.TryParse(objective.Groups["index"].Value, out int objectiveIndex)
                        && int.TryParse(objective.Groups["count"].Value, out int completeCount)
                        && completeCount > 0)
                    {
                        quest.Objectives.Add(new ObjectiveDefinition(objectiveIndex, completeCount,
                            Field(line, "ObjectiveType") ?? "QuestObjectiveType_Primary",
                            Field(line, "BattleZoneObjectiveGameType") ?? "",
                            Field(line, "BattleZoneObjective") ?? "",
                            IntField(line, "ExtraParam"), IntField(line, "ExtraParam2")));
                        continue;
                    }
                    var match = RequestItemRx.Match(line);
                    if (match.Success
                        && int.TryParse(match.Groups["item"].Value, out int item)
                        && int.TryParse(match.Groups["count"].Value, out int count)
                        && item > 0 && count > 0)
                        quest.EnterRequestItems.Add(new ItemRequirement(item, count));
                }
            }
            _audit = new AuditResult(questObjects, rewardStages, nonPrimaryRewardStages,
                _quests.Values.Count(q => q.ResultDrop), extractionErrors);
            _logger.LogInformation(
                "[QuestCatalog] loaded {0} quests ({1} instance maps), {2} reward stages, {3} extraction errors from {4}.",
                _quests.Count, _quests.Values.Count(q => q.Level != null), rewardStages, extractionErrors, path);
        }
        catch (Exception ex)
        {
            _quests.Clear();
            _logger.LogError("[QuestCatalog] failed to load quest metadata: {0}", ex.Message);
        }
    }

    private void LoadClientMaps()
    {
        string root = ClientFolder.Root;
        string mapRoot = ClientFolder.Resolve(root, @"HuxleyGame\Content\Maps");
        if (!Directory.Exists(mapRoot))
        {
            _logger.LogWarning("[QuestCatalog] client map root not found: {0}", mapRoot);
            return;
        }
        try
        {
            _clientMaps = Directory.EnumerateFiles(mapRoot, "*.war", ClientFolder.AnyCase)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
            _logger.LogInformation("[QuestCatalog] indexed {0} client map packages under {1}.", _clientMaps.Count, mapRoot);

            var required = _quests.Values.Where(q => q.Level != null).Select(q => q.Level!)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var missing = required.Where(level => !_clientMaps.Contains(level)).Order().ToList();
            _logger.LogInformation("[QuestCatalog] instance-map coverage: {0}/{1} quest levels present.",
                required.Count - missing.Count, required.Count);
            if (missing.Count > 0)
                _logger.LogWarning("[QuestCatalog] {0} referenced map package(s) missing: {1}{2}",
                    missing.Count, string.Join(", ", missing.Take(12)), missing.Count > 12 ? ", ..." : "");
        }
        catch (Exception ex) { _logger.LogWarning("[QuestCatalog] client map index failed: {0}", ex.Message); }
    }

    // One HuxleyQuestData object while it is being read. Defaults are the class defaults: cooked
    // exports omit properties that keep them (EQuestType's zero value is QuestType_City, so a
    // missing QuestType line is an ordinary city quest).
    private sealed class Pending
    {
        public int EntryIndex, MinPlayers = 1, MaxPlayers = 1, ExitZoneIndex = -1, ExitAreaIndex = -1;
        // Default__HuxleyQuestData: MaxLife -1, DurabilityDecParam 9, RecommendLevelFactor 1,
        // bUseQuestRoom true (analysis/HuxleyGame-script-cdos.txt).
        public int ContaminationAmount, RecommendLevel, MaxLife = -1, DurabilityDecParam = 9;
        public int DropGradeS, DropGradeA, DropGradeB, DropGradeC;
        public float RecommendLevelFactor = 1;
        public bool RepeatQuest, UseContamination, UseQuestRoom = true, ResultDrop;
        public string QuestName = "", QuestType = "QuestType_City", QuestSubType = "QuestSub_Single",
            ShareType = "Share_None", Level = "";
        public List<ItemRequirement> EnterRequestItems = [];
        public List<int> QuestItems = [], RemoveItems = [], TimeLimits = [];
        public List<ObjectiveDefinition> Objectives = [];

        public Quest Build() => new(EntryIndex, QuestName,
            string.IsNullOrWhiteSpace(Level) ? null : Level, QuestType, QuestSubType, ShareType,
            (byte)Math.Clamp(MinPlayers, 1, byte.MaxValue),
            (byte)Math.Clamp(MaxPlayers, 1, byte.MaxValue), RepeatQuest,
            EnterRequestItems.ToArray(), QuestItems.ToArray(), RemoveItems.ToArray(),
            Objectives.OrderBy(x => x.Index).ToArray(), ExitZoneIndex, ExitAreaIndex,
            UseContamination, ContaminationAmount, RecommendLevel,
            RecommendLevelFactor, MaxLife, UseQuestRoom, DurabilityDecParam,
            ResultDrop, DropGradeS, DropGradeA, DropGradeB, DropGradeC,
            TimeLimits.ToArray());
    }

    // Reads one property; false leaves the line to the objective/request-item patterns.
    private delegate bool FieldReader(Pending quest, string value, string line);

    // Exported property name (the text before '=' or '[') -> how a quest reads it.
    private static readonly Dictionary<string, FieldReader> Fields = new(StringComparer.Ordinal)
    {
        ["QuestName"] = (q, v, _) => { q.QuestName = Unquote(v); return true; },
        ["Level"] = (q, v, _) => { q.Level = Unquote(v); return true; },
        ["QuestType"] = (q, v, _) => { q.QuestType = v; return true; },
        ["QuestSubType"] = (q, v, _) => { q.QuestSubType = v; return true; },
        ["ShareType"] = (q, v, _) => { q.ShareType = v; return true; },
        ["MinPlayers"] = Int((q, n) => q.MinPlayers = n),
        ["MaxPlayers"] = Int((q, n) => q.MaxPlayers = n),
        ["ExitZoneIndex"] = Int((q, n) => q.ExitZoneIndex = n),
        ["ExitAreaIndex"] = Int((q, n) => q.ExitAreaIndex = n),
        ["ContaminationAmount"] = Int((q, n) => q.ContaminationAmount = n),
        ["RecommendLevel"] = Int((q, n) => q.RecommendLevel = n),
        ["RecommendLevelFactor"] = (q, v, _) => float.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out q.RecommendLevelFactor),
        ["MaxLife"] = Int((q, n) => q.MaxLife = n),
        ["DurabilityDecParam"] = Int((q, n) => q.DurabilityDecParam = n),
        ["DropGrade_S"] = Int((q, n) => q.DropGradeS = n),
        ["DropGrade_A"] = Int((q, n) => q.DropGradeA = n),
        ["DropGrade_B"] = Int((q, n) => q.DropGradeB = n),
        ["DropGrade_C"] = Int((q, n) => q.DropGradeC = n),
        ["EntryIndex"] = Int((q, n) => q.EntryIndex = n),
        ["TimeLimits"] = PositiveInt((q, n) => q.TimeLimits.Add(n)),
        ["QuestItems"] = PositiveInt((q, n) => q.QuestItems.Add(n)),
        ["RemoveItems"] = PositiveInt((q, n) => q.RemoveItems.Add(n)),
        ["bRepeatQuest"] = Flag(q => q.RepeatQuest = true),
        ["bUseContamination"] = Flag(q => q.UseContamination = true),
        ["bUseQuestRoom"] = (q, v, _) => { q.UseQuestRoom = !v.Equals("false", StringComparison.OrdinalIgnoreCase); return true; },
        ["ResultDrop"] = Flag(q => q.ResultDrop = true),
    };

    private static FieldReader Int(Action<Pending, int> set) =>
        (q, v, _) => { if (!int.TryParse(v, out int n)) return false; set(q, n); return true; };

    // Array elements ("QuestItems[0]=601234"); zero and negative entries are padding.
    private static FieldReader PositiveInt(Action<Pending, int> add) =>
        (q, v, _) => { if (!int.TryParse(v, out int n) || n <= 0) return false; add(q, n); return true; };

    private static FieldReader Flag(Action<Pending> set) =>
        (q, v, _) => { if (!v.Equals("true", StringComparison.OrdinalIgnoreCase)) return false; set(q); return true; };

    private static string Unquote(string value) => value.Trim().Trim('"');

    private static string? Field(string line, string name)
    {
        string marker = name + "=";
        int start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int comma = line.IndexOf(',', start);
        int close = line.IndexOf(')', start);
        int end = comma < 0 ? close : close < 0 ? comma : Math.Min(comma, close);
        return end > start ? line[start..end] : null;
    }

    private static int IntField(string line, string name) =>
        int.TryParse(Field(line, name), out int value) ? value : 0;
}
