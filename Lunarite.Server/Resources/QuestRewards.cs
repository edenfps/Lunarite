using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using Lunarite.Storage;

namespace Lunarite.Resources;

// Exact quest completion rewards from HuxleyQuestTable.upk's recovered object graph.
public sealed class QuestRewards
{
    private readonly ILogger _logger;
    private readonly CharacterStore _characterStore;

    public sealed record AuditResult(int QuestObjects, int RewardEntries, int Modifiers,
        int OptionalGroups, int OptionalChoices, int InvalidQuantities, int UnknownItems,
        int ExtractionErrors, int CompletionModifierReferences, int MissingModifiers,
        int NonCompletionModifiers);

    public sealed class Reward
    {
        public long BP;
        public long Money;
        public long LicensePoints;
        public List<(int Item, int Count)> Items = [];
        public List<(int Item, int Count)> OptionalItems = [];
    }

    private static readonly Regex QuestRefRx = new(
        @"^// Reference: HuxleyQuestData'HuxleyQuestTable\.(?<table>[^.]+)\.(?<quest>HuxleyQuestData(?:_\d+)?)'$",
        RegexOptions.Compiled);
    private static readonly Regex ModifierRefRx = new(
        @"^// Reference: HuxleyUserPropertyModifier_Give'HuxleyQuestTable\.(?<table>[^.]+)\.(?<quest>HuxleyQuestData(?:_\d+)?)\.(?<modifier>HuxleyUserPropertyModifier_Give(?:_\d+)?)'$",
        RegexOptions.Compiled);
    private static readonly Regex TransactionModifierRx = new(
        @"Transactions=(?<modifier>HuxleyUserPropertyModifier_Give(?:_\d+)?)",
        RegexOptions.Compiled);

    private readonly object _lock = new();
    private readonly Dictionary<(string Table, int Quest), Reward> _rewards = [];
    private AuditResult _audit = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private bool _loaded;

    private readonly ItemCatalog _itemCatalog;

    public QuestRewards(ILogger<QuestRewards> logger, CharacterStore characterStore, ItemCatalog itemCatalog)
    {
        _logger = logger;
        _characterStore = characterStore;
        _itemCatalog = itemCatalog;
    }

    public Reward Get(int pid, int questIndex)
    {
        byte species = _characterStore.Get(pid)?.Index ?? 0;
        return GetForSpecies(species, questIndex);
    }

    // Character TypeIndex values 1/6 are Sapiens and 11/16 are Alternatives. Keep this
    // independently testable so quest completion cannot silently select the other faction's
    // reward graph when roster persistence changes.
    public Reward GetForSpecies(byte species, int questIndex)
    {
        EnsureLoaded();
        string table = species is > 0 and < 10 ? "SapiensQuests" : "AlternativesQuests";
        if (!_rewards.TryGetValue((table, questIndex), out Reward? reward)) return new Reward();
        return new Reward
        {
            BP = reward.BP,
            Money = reward.Money,
            LicensePoints = reward.LicensePoints,
            Items = [.. reward.Items],
            OptionalItems = [.. reward.OptionalItems]
        };
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

    public AuditResult Audit()
    {
        EnsureLoaded();
        return _audit;
    }

    public static bool TrySelectOptional(Reward reward, int slot)
    {
        if (reward.OptionalItems.Count == 0) return true;
        if (slot < 0 || slot >= reward.OptionalItems.Count) return false;
        reward.Items.Add(reward.OptionalItems[slot]);
        return true;
    }

    private void Load()
    {
        string? path = FindDump();
        if (path == null)
        {
            _logger.LogError("[QuestRewards] data/tables/HuxleyQuestTable.txt not found.");
            return;
        }

        var completionModifiers = new HashSet<(string Table, string Quest, string Modifier)>();
        var questIds = MapQuestEntries(path, completionModifiers, out int optionalGroups, out int optionalChoices);
        var seenModifiers = new HashSet<(string Table, string Quest, string Modifier)>();
        int modifierCount = AggregateCompletionRewards(path, questIds, completionModifiers, seenModifiers);
        RecordAudit(path, questIds, completionModifiers, seenModifiers, modifierCount,
            optionalGroups, optionalChoices);
    }

    // First pass maps each species-table object name to its wire EntryIndex, collects the
    // Transactions[] modifier references, and stores each quest's optional reward choices.
    private Dictionary<(string Table, string Object), int> MapQuestEntries(string path,
        HashSet<(string Table, string Quest, string Modifier)> completionModifiers,
        out int optionalGroups, out int optionalChoices)
    {
        var questIds = new Dictionary<(string Table, string Object), int>();
        string pendingTable = "", pendingQuest = "";
        bool inQuest = false;
        int depth = 0, entryIndex = 0;
        optionalGroups = 0;
        optionalChoices = 0;
        var optionalItems = new List<(int Item, int Count)>();

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (!inQuest)
            {
                Match reference = QuestRefRx.Match(line);
                if (reference.Success)
                {
                    pendingTable = reference.Groups["table"].Value;
                    pendingQuest = reference.Groups["quest"].Value;
                    continue;
                }
                if (pendingQuest.Length != 0 && line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("class=HuxleyGame.HuxleyQuestData", StringComparison.OrdinalIgnoreCase))
                {
                    inQuest = true; depth = 1; entryIndex = 0; optionalItems = [];
                }
                continue;
            }

            if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase)) { depth++; continue; }
            if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
            {
                if (--depth == 0)
                {
                    if (entryIndex != 0)
                    {
                        questIds[(pendingTable, pendingQuest)] = entryIndex;
                        if (optionalItems.Count != 0)
                        {
                            if (!_rewards.TryGetValue((pendingTable, entryIndex), out Reward? reward))
                                reward = _rewards[(pendingTable, entryIndex)] = new Reward();
                            reward.OptionalItems.AddRange(optionalItems);
                            optionalGroups++;
                            optionalChoices += optionalItems.Count;
                        }
                    }
                    inQuest = false; pendingTable = pendingQuest = "";
                }
                continue;
            }
            if (depth == 1 && line.StartsWith("EntryIndex=", StringComparison.Ordinal)
                && int.TryParse(line["EntryIndex=".Length..], out int parsed)) entryIndex = parsed;
            else if (depth == 1 && line.StartsWith("Transactions[", StringComparison.Ordinal))
            {
                foreach (Match match in TransactionModifierRx.Matches(line))
                    completionModifiers.Add((pendingTable, pendingQuest, match.Groups["modifier"].Value));
            }
            else if (depth == 1 && TryOptionalItem(line, out int item, out int count))
                optionalItems.Add((item, count));
        }
        return questIds;
    }

    // Second pass aggregates every Modifier_Give nested under the quest. Exported objectives in
    // this build all have Transactions=none, so these are precisely the completion rewards.
    // Returns the number of modifiers credited.
    private int AggregateCompletionRewards(string path,
        Dictionary<(string Table, string Object), int> questIds,
        HashSet<(string Table, string Quest, string Modifier)> completionModifiers,
        HashSet<(string Table, string Quest, string Modifier)> seenModifiers)
    {
        string modifierTable = "", modifierQuest = "";
        bool inModifier = false;
        int depth = 0, modifierCount = 0;
        var current = new Reward();
        bool isCompletionModifier = false;

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (!inModifier)
            {
                Match reference = ModifierRefRx.Match(line);
                if (reference.Success)
                {
                    modifierTable = reference.Groups["table"].Value;
                    modifierQuest = reference.Groups["quest"].Value;
                    var key = (modifierTable, modifierQuest, reference.Groups["modifier"].Value);
                    seenModifiers.Add(key);
                    isCompletionModifier = completionModifiers.Contains(key);
                    continue;
                }
                if (modifierQuest.Length != 0 && line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase)
                    && line.Contains("class=HuxleyGame.HuxleyUserPropertyModifier_Give", StringComparison.OrdinalIgnoreCase))
                {
                    inModifier = true; depth = 1; current = new Reward();
                }
                continue;
            }

            if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase)) { depth++; continue; }
            if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
            {
                if (--depth == 0)
                {
                    if (isCompletionModifier
                        && questIds.TryGetValue((modifierTable, modifierQuest), out int questIndex))
                    {
                        if (!_rewards.TryGetValue((modifierTable, questIndex), out Reward? total))
                            total = _rewards[(modifierTable, questIndex)] = new Reward();
                        total.BP += current.BP;
                        total.Money += current.Money;
                        total.LicensePoints += current.LicensePoints;
                        total.Items.AddRange(current.Items);
                        modifierCount++;
                    }
                    inModifier = false; modifierTable = modifierQuest = "";
                }
                continue;
            }

            if (TryLong(line, "BP=", out long value)) current.BP += value;
            else if (TryLong(line, "Money=", out value)) current.Money += value;
            else if (TryLong(line, "LicensePoint=", out value)) current.LicensePoints += value;
            else if (TryInt(line, "ItemIndex=", out int item)) current.Items.Add((item, 1));
            else if (TryInt(line, "Quantity=", out int count) && current.Items.Count != 0)
            {
                var last = current.Items[^1];
                current.Items[^1] = (last.Item, count);
            }
        }
        return modifierCount;
    }

    private void RecordAudit(string path, Dictionary<(string Table, string Object), int> questIds,
        HashSet<(string Table, string Quest, string Modifier)> completionModifiers,
        HashSet<(string Table, string Quest, string Modifier)> seenModifiers,
        int modifierCount, int optionalGroups, int optionalChoices)
    {
        int invalidQuantities = _rewards.Values.Sum(reward => reward.Items.Count(x => x.Count <= 0)
            + reward.OptionalItems.Count(x => x.Count <= 0));
        int unknownItems = _rewards.Values.SelectMany(reward => reward.Items.Concat(reward.OptionalItems))
            .Select(x => x.Item).Distinct().Count(item => _itemCatalog.Get(item) == null);
        int extractionErrors = File.ReadLines(path).Count(line => line.Contains("/* ERROR:", StringComparison.Ordinal));
        _audit = new AuditResult(questIds.Count, _rewards.Count, modifierCount,
            optionalGroups, optionalChoices, invalidQuantities, unknownItems, extractionErrors,
            completionModifiers.Count, completionModifiers.Except(seenModifiers).Count(),
            seenModifiers.Except(completionModifiers).Count());
        _logger.LogInformation(
            "[QuestRewards] loaded {0} modifiers for {1} species/quest entries; {2} optional groups / {3} choices ({4} table objects).",
            modifierCount, _rewards.Count, optionalGroups, optionalChoices, questIds.Count);
    }

    private static bool TryLong(string line, string key, out long value)
    {
        value = 0;
        return line.StartsWith(key, StringComparison.Ordinal) && long.TryParse(line[key.Length..], out value);
    }

    private static bool TryInt(string line, string key, out int value)
    {
        value = 0;
        return line.StartsWith(key, StringComparison.Ordinal) && int.TryParse(line[key.Length..], out value);
    }

    private static bool TryOptionalItem(string line, out int item, out int count)
    {
        item = count = 0;
        if (!line.StartsWith("OptionalRewardItems[", StringComparison.Ordinal)) return false;
        int itemAt = line.IndexOf("ItemIndex=", StringComparison.Ordinal);
        int countAt = line.IndexOf("ItemCount=", StringComparison.Ordinal);
        if (itemAt < 0 || countAt < 0) return false;
        itemAt += "ItemIndex=".Length;
        countAt += "ItemCount=".Length;
        int itemEnd = line.IndexOf(',', itemAt);
        int countEnd = line.IndexOf(')', countAt);
        return itemEnd > itemAt && countEnd > countAt
            && int.TryParse(line[itemAt..itemEnd], out item)
            && int.TryParse(line[countAt..countEnd], out count);
    }

    private static string? FindDump()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            // HuxleyQuestData.txt is produced by the quest-aware extractor and preserves native
            // QuestTransaction arrays. The older general table dump contains hundreds of inline
            // decompiler failures and must never win merely because both files are present.
            string path = Path.Combine(dir, "HuxleyQuestData.txt");
            if (File.Exists(path)) return path;
            path = Path.Combine(dir, "data", "HuxleyQuestData.txt");
            if (File.Exists(path)) return path;
            path = Path.Combine(dir, "tables", "HuxleyQuestTable.txt");
            if (File.Exists(path)) return path;
            path = Path.Combine(dir, "data", "tables", "HuxleyQuestTable.txt");
            if (File.Exists(path)) return path;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }
}
