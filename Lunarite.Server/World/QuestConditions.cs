using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

// Evaluates the same exported HuxleyCondition graph the retail client scripts define. The client
// does not evaluate possible quests itself; it trusts op35, so this is master-server behavior.
public sealed class QuestConditions
{
    private readonly ILogger _logger;
    private readonly CharacterStore _characterStore;
    private readonly PlayerStore _playerStore;
    private readonly QuestStore _questStore;

    public sealed record Coverage(int Rules, IReadOnlyList<string> UnsupportedConditions,
        IReadOnlyList<string> UnsupportedProperties);

    public sealed record ChainCoverage(int Rules, IReadOnlyList<int> MissingCatalogEntries,
        IReadOnlyList<(int Quest, int Prerequisite)> MissingPrerequisites,
        IReadOnlyList<IReadOnlyList<int>> Cycles);

    public sealed record ReachabilityCoverage(int Rules, IReadOnlyList<int> UnreachableQuests);

    private static readonly HashSet<string> SupportedConditions =
        ["HuxleyCondition_All", "HuxleyCondition_Any", "HuxleyCondition_Equal", "HuxleyCondition_GreaterThan"];
    private static readonly HashSet<string> SupportedProperties =
        ["HuxleyUserProperty_Level", "HuxleyUserProperty_CombatStyle", "HuxleyUserProperty_CompletedQuest"];

    private sealed class Obj
    {
        public string Class = "";
        public string? Property;
        public List<string> Conditions = [];
        public Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Rule
    {
        public int EntryIndex;
        public int RecommendLevel;
        public string Table = "";
        public string? Condition;
        public Dictionary<string, Obj> Objects = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record Context(int Level, long Bp, long Lp, long Money, byte Style, byte Character,
        IReadOnlySet<int> Acquired, IReadOnlySet<int> Completed, IReadOnlySet<int> Items);

    private static readonly Regex BeginRx = new(
        "name=\"(?<name>[^\"]+)\"\\s+class=HuxleyGame\\.(?<class>[^\\s]+)", RegexOptions.Compiled);
    private readonly Dictionary<int, Rule> _rules = [];
    private readonly object _lock = new();
    private bool _loaded;

    private readonly QuestCatalog _questCatalog;

    public QuestConditions(ILogger<QuestConditions> logger, CharacterStore characterStore,
        PlayerStore playerStore, QuestStore questStore, QuestCatalog questCatalog)
    {
        _logger = logger;
        _characterStore = characterStore;
        _playerStore = playerStore;
        _questStore = questStore;
        _questCatalog = questCatalog;
    }

    public List<int> Available(int pid)
    {
        EnsureLoaded();
        var progress = _playerStore.GetProgress(pid);
        var character = _characterStore.Get(pid);
        var acquired = _questStore.Acquired(pid).ToHashSet();
        var completed = _questStore.Completed(pid);
        var items = _playerStore.GetInventory(pid, () => []).Select(i => i.ItemId)
            .Concat(_playerStore.GetItems(pid).Select(i => i.item)).ToHashSet();
        var ctx = new Context(progress.Level, progress.BP, progress.LP, progress.Money, character?.Style ?? 0,
            character?.Index ?? 0, acquired, completed, items);
        string table = ctx.Character is > 0 and < 10 ? "SapiensQuests" : "AlternativesQuests";

        return _rules.Values
            .Where(r => r.Table.Equals(table, StringComparison.OrdinalIgnoreCase))
            .Where(r => !acquired.Contains(r.EntryIndex) && !completed.Contains(r.EntryIndex))
            // Never offer a quest whose map package the client lacks (the table references four
            // FQ-83000x packages neither supplied client contains).
            .Where(r => _questCatalog.Get(r.EntryIndex) is not { Level: { } level }
                || _questCatalog.ClientHasMap(level))
            .Where(r => Evaluate(r, ctx))
            .Select(r => r.EntryIndex)
            .Order()
            .ToList();
    }

    // Condition and property classes in the exported graphs that Evaluate does not support.
    public Coverage AuditCoverage()
    {
        EnsureLoaded();
        lock (_lock)
        {
            var conditionClasses = _rules.Values.SelectMany(ReachableObjects)
                .Select(o => o.Class).Where(c => c.StartsWith("HuxleyCondition_", StringComparison.Ordinal))
                .Distinct().Where(c => !SupportedConditions.Contains(c)).Order().ToList();
            var propertyClasses = _rules.Values.SelectMany(ReachableObjects)
                .Select(o => o.Class).Where(c => c.StartsWith("HuxleyUserProperty_", StringComparison.Ordinal))
                .Distinct().Where(c => !SupportedProperties.Contains(c)).Order().ToList();
            return new Coverage(_rules.Count, conditionClasses, propertyClasses);
        }
    }

    // Checks the CompletedQuest dependency graph on its own: missing prerequisites and cycles.
    public ChainCoverage AuditChains()
    {
        EnsureLoaded();
        lock (_lock)
        {
            var ids = _rules.Keys.ToHashSet();
            var missingCatalog = ids.Where(id => _questCatalog.Get(id) == null).Order().ToList();
            var edges = _rules.Values.ToDictionary(r => r.EntryIndex, r => ReachableObjects(r)
                .Where(o => o.Class == "HuxleyUserProperty_CompletedQuest")
                .Select(o => int.TryParse(o.Values.GetValueOrDefault("QuestIndex"), out int id) ? id : 0)
                .Where(id => id > 0).Distinct().ToList());
            var missingPrerequisites = edges.SelectMany(pair => pair.Value
                    .Where(prerequisite => !ids.Contains(prerequisite))
                    .Select(prerequisite => (pair.Key, prerequisite)))
                .OrderBy(x => x.Key).ThenBy(x => x.prerequisite).ToList();

            var cycles = new List<IReadOnlyList<int>>();
            var state = new Dictionary<int, byte>(); // 1 visiting, 2 complete
            var stack = new List<int>();
            void Visit(int id)
            {
                if (state.GetValueOrDefault(id) == 2) return;
                if (state.GetValueOrDefault(id) == 1)
                {
                    int start = stack.IndexOf(id);
                    if (start >= 0) cycles.Add(stack.Skip(start).Append(id).ToArray());
                    return;
                }
                state[id] = 1; stack.Add(id);
                if (edges.TryGetValue(id, out var prerequisites))
                    foreach (int prerequisite in prerequisites.Where(ids.Contains)) Visit(prerequisite);
                stack.RemoveAt(stack.Count - 1); state[id] = 2;
            }
            foreach (int id in ids.Order()) Visit(id);
            return new ChainCoverage(_rules.Count, missingCatalog, missingPrerequisites, cycles);
        }
    }

    // Quests no legal character state can unlock. Completed quests come only from the same
    // faction table, so cross-faction prerequisites count as unreachable.
    public ReachabilityCoverage AuditReachability()
    {
        EnsureLoaded();
        lock (_lock)
        {
            var unreachable = new List<int>();
            foreach (Rule rule in _rules.Values.OrderBy(x => x.EntryIndex))
            {
                var factionQuests = _rules.Values
                    .Where(x => x.Table.Equals(rule.Table, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.EntryIndex).ToHashSet();
                byte character = rule.Table.Equals("SapiensQuests", StringComparison.OrdinalIgnoreCase)
                    ? (byte)1 : (byte)11;
                bool reachable = false;
                for (int level = 1; level <= 30 && !reachable; level++)
                    for (byte style = 1; style <= 3 && !reachable; style++)
                    {
                        var context = new Context(level, long.MaxValue, long.MaxValue, long.MaxValue,
                            style, character, new HashSet<int>(), factionQuests, new HashSet<int>());
                        reachable = Evaluate(rule, context);
                    }
                if (!reachable) unreachable.Add(rule.EntryIndex);
            }
            return new ReachabilityCoverage(_rules.Count, unreachable);
        }
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            Load();
        }
    }

    private void Load()
    {
        string? path = FindDump();
        if (path == null) { _logger.LogError("[QuestConditions] HuxleyQuestData.txt not found."); return; }
        try
        {
            string pendingTable = "";
            Rule? rule = null;
            int depth = 0;
            var stack = new Stack<Obj>();

            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (rule == null && line.StartsWith("// Reference: HuxleyQuestData'"))
                {
                    pendingTable = line.Contains(".SapiensQuests.") ? "SapiensQuests"
                        : line.Contains(".AlternativesQuests.") ? "AlternativesQuests" : "";
                    continue;
                }
                if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
                {
                    if (rule == null && line.Contains("class=HuxleyGame.HuxleyQuestData", StringComparison.OrdinalIgnoreCase))
                    {
                        rule = new Rule { Table = pendingTable }; depth = 1; stack.Clear();
                    }
                    else if (rule != null)
                    {
                        depth++;
                        var match = BeginRx.Match(line);
                        var obj = new Obj { Class = match.Success ? match.Groups["class"].Value : "" };
                        if (match.Success) rule.Objects[match.Groups["name"].Value] = obj;
                        stack.Push(obj);
                    }
                    continue;
                }
                if (rule == null) continue;
                if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
                {
                    if (depth > 1 && stack.Count > 0) stack.Pop();
                    if (--depth == 0)
                    {
                        if (rule.EntryIndex != 0 && rule.Table.Length > 0) _rules[rule.EntryIndex] = rule;
                        rule = null;
                    }
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0) continue;
                string key = line[..equals];
                string value = line[(equals + 1)..].Trim().Trim('"');
                if (depth == 1)
                {
                    switch (key)
                    {
                        case "EntryIndex":
                            int.TryParse(value, out rule.EntryIndex);
                            break;
                        case "RecommendLevel":
                            int.TryParse(value, out rule.RecommendLevel);
                            break;
                        case "Condition" when !value.Equals("none", StringComparison.OrdinalIgnoreCase):
                            rule.Condition = value;
                            break;
                    }
                }
                else if (stack.Count > 0)
                {
                    Obj obj = stack.Peek();
                    if (key.StartsWith("Conditions[")) obj.Conditions.Add(value);
                    else if (key == "Property") obj.Property = value;
                    else obj.Values[key] = value;
                }
            }
            var coverage = AuditCoverage();
            if (coverage.UnsupportedConditions.Count != 0 || coverage.UnsupportedProperties.Count != 0)
                _logger.LogError("[QuestConditions] unsupported exported graph classes: conditions={0}; properties={1}.",
                    string.Join(",", coverage.UnsupportedConditions), string.Join(",", coverage.UnsupportedProperties));
            _logger.LogInformation("[QuestConditions] loaded {0} quest condition graphs; exported class coverage complete={1}.",
                _rules.Count, coverage.UnsupportedConditions.Count == 0 && coverage.UnsupportedProperties.Count == 0);
        }
        catch (Exception ex)
        {
            _rules.Clear();
            _logger.LogError("[QuestConditions] load failed: {0}", ex.Message);
        }
    }

    private static bool Evaluate(Rule rule, Context ctx)
    {
        // RecommendLevel is not a gate: HuxleyUIQuestState uses it only for the difficulty label.
        // Level requirements are explicit HuxleyCondition_GreaterThan(HuxleyUserProperty_Level) nodes.
        if (rule.Condition == null) return true;
        return EvaluateObject(rule, rule.Condition, ctx, []);
    }

    // Only objects reachable from Condition form the availability graph; a quest also nests
    // reward modifier/property objects that are not gates.
    private static IEnumerable<Obj> ReachableObjects(Rule rule)
    {
        if (rule.Condition == null) yield break;
        var pending = new Stack<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(rule.Condition);
        while (pending.Count != 0)
        {
            string name = pending.Pop();
            if (!seen.Add(name) || !rule.Objects.TryGetValue(name, out Obj? obj)) continue;
            yield return obj;
            foreach (string child in obj.Conditions) pending.Push(child);
            if (obj.Property != null) pending.Push(obj.Property);
        }
    }

    private static bool EvaluateObject(Rule rule, string name, Context ctx, HashSet<string> visiting)
    {
        if (!visiting.Add(name) || !rule.Objects.TryGetValue(name, out Obj? obj)) return false;
        bool result;
        if (obj.Class == "HuxleyCondition_All")
            result = obj.Conditions.All(child => EvaluateObject(rule, child, ctx, visiting));
        else if (obj.Class == "HuxleyCondition_Any")
            result = obj.Conditions.Any(child => EvaluateObject(rule, child, ctx, visiting));
        else if (obj.Class is "HuxleyCondition_Equal" or "HuxleyCondition_GreaterThan")
        {
            if (obj.Property == null || !rule.Objects.TryGetValue(obj.Property, out Obj? property)
                || !PropertyValues(property, ctx, out long actual, out long expected))
                result = false;
            else
                result = obj.Class == "HuxleyCondition_Equal" ? actual == expected : actual >= expected;
        }
        else result = false;
        visiting.Remove(name);
        return result;
    }

    private static bool PropertyValues(Obj obj, Context ctx, out long actual, out long expected)
    {
        actual = expected = 0;
        switch (obj.Class)
        {
            case "HuxleyUserProperty_Level":
                actual = ctx.Level; return TryLong(obj, "Level", out expected);
            case "HuxleyUserProperty_BP":
                actual = ctx.Bp; return TryLong(obj, "BP", out expected);
            case "HuxleyUserProperty_CR":
                actual = ctx.Money; return TryLong(obj, "Money", out expected);
            case "HuxleyUserProperty_LP":
                actual = ctx.Lp; return TryLong(obj, "LicensePoint", out expected);
            case "HuxleyUserProperty_CombatStyle":
                actual = ctx.Style;
                if (!obj.Values.TryGetValue("CombatStyle", out string? style)) return false;
                expected = style switch { "CombatStyle_Enforcer" => 1, "CombatStyle_Avenger" => 2,
                    "CombatStyle_Phantom" => 3, _ => 0 };
                return true;
            case "HuxleyUserProperty_CompletedQuest":
                if (!TryLong(obj, "QuestIndex", out expected)) return false;
                actual = ctx.Completed.Contains((int)expected) ? expected : 0; return true;
            case "HuxleyUserProperty_Quest":
                if (!TryLong(obj, "QuestIndex", out expected)) return false;
                actual = ctx.Acquired.Contains((int)expected) ? expected : 0; return true;
            case "HuxleyUserProperty_Item":
                if (!TryLong(obj, "ItemIndex", out expected)) return false;
                actual = ctx.Items.Contains((int)expected) ? expected : 0; return true;
            case "HuxleyUserProperty_Gender":
                expected = string.Equals(obj.Values.GetValueOrDefault("bIsFemale"), "true",
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                actual = ctx.Character is 6 or 16 ? 1 : 0; return true;
            default:
                return false;
        }
    }

    private static bool TryLong(Obj obj, string key, out long value) =>
        long.TryParse(obj.Values.GetValueOrDefault(key), out value);

    private static string? FindDump()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 7 && dir != null; i++)
        {
            foreach (string path in new[]
            {
                Path.Combine(dir, "HuxleyQuestData.txt"), Path.Combine(dir, "data", "HuxleyQuestData.txt")
            })
                if (File.Exists(path)) return path;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }
}
