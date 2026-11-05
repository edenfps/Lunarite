using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

// Authoritative pub-minigame definitions from the exported HuxleyPersonalBuffTable.
public sealed class PersonalBuffCatalog
{
    private readonly ILogger _logger;

    public sealed record Buff(int Id, byte Group, byte ModifierType, byte CanBeCumulated, int DurationSeconds);

    public sealed record Choice(int BuffId, byte GoalRatio, byte MaxLevel);

    public sealed record Setter(int Id, int Price, byte GaugeSpeed, int SideEffectProbability,
        Choice Great, Choice Success, Choice Handicap, Choice SideEffect);

    private static readonly Dictionary<int, Buff> Buffs = [];
    private static readonly Dictionary<int, Setter> Setters = [];
    private static readonly object Lock = new();
    private bool _loaded;

    public PersonalBuffCatalog(ILogger<PersonalBuffCatalog> logger)
    {
        _logger = logger;
    }

    public Buff? GetBuff(int id) { EnsureLoaded(); return Buffs.GetValueOrDefault(id); }

    public Setter? GetSetter(int id) { EnsureLoaded(); return Setters.GetValueOrDefault(id); }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Lock)
        {
            if (_loaded) return;
            _loaded = true;
            string? dir = AppContext.BaseDirectory, path = null;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "data", "tables", "HuxleyPersonalBuffTable.txt");
                if (File.Exists(candidate)) { path = candidate; break; }
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            if (path == null)
            {
                _logger.LogError("[PersonalBuffCatalog] exported table not found; pub drinks are disabled.");
                return;
            }

            Pending? pending = null;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
                {
                    pending = line.Contains("class=HuxleyGame.HuxleyPersonalBuffSetterData", StringComparison.Ordinal)
                        ? new Pending { IsSetter = true }
                        : line.Contains("class=HuxleyGame.HuxleyPersonalBuffData", StringComparison.Ordinal)
                            ? new Pending() : null;
                    continue;
                }
                if (pending == null) continue;
                if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
                {
                    if (pending.EntryIndex > 0)
                    {
                        if (pending.IsSetter) Setters[pending.EntryIndex] = pending.BuildSetter();
                        else Buffs[pending.EntryIndex] = pending.BuildBuff();
                    }
                    pending = null;
                    continue;
                }
                int split = line.IndexOfAny(['=', '[']);
                if (split > 0 && Fields.TryGetValue(line[..split], out var read))
                    read(pending, line[(line.IndexOf('=') + 1)..], line);
            }
            _logger.LogInformation("[PersonalBuffCatalog] loaded {0} buffs and {1} setters.", Buffs.Count, Setters.Count);
        }
    }

    // One HuxleyPersonalBuffData or HuxleyPersonalBuffSetterData object while it is being read.
    private sealed class Pending
    {
        public bool IsSetter;
        public int EntryIndex, GroupCode, ModifierTypeCode, DurationSeconds, Price, SideEffectProbability;
        public int DefaultGaugeSpeed = 1;
        public byte CanBeCumulated;
        // GreateSuccessBuff is the retail property's own spelling.
        public Choice GreateSuccessBuff = new(0, 0, 1), SuccessBuff = new(0, 0, 1),
            HandicapBuff = new(0, 0, 1), SideEffectBuff = new(0, 0, 1);

        public Buff BuildBuff() => new(EntryIndex, checked((byte)GroupCode),
            checked((byte)ModifierTypeCode), CanBeCumulated, DurationSeconds);

        public Setter BuildSetter() => new(EntryIndex, Price, checked((byte)DefaultGaugeSpeed),
            SideEffectProbability, GreateSuccessBuff, SuccessBuff, HandicapBuff, SideEffectBuff);
    }

    private delegate void FieldReader(Pending buff, string value, string line);

    // Exported property name (the text before '=' or '[') -> how a buff or setter reads it.
    private static readonly Dictionary<string, FieldReader> Fields = new(StringComparer.Ordinal)
    {
        ["EntryIndex"] = Int((b, n) => b.EntryIndex = n),
        ["GroupCode"] = Int((b, n) => b.GroupCode = n),
        ["ModifierTypeCode"] = Int((b, n) => b.ModifierTypeCode = n),
        ["DurationSeconds"] = Int((b, n) => b.DurationSeconds = n),
        ["Price"] = Int((b, n) => b.Price = n),
        ["DefaultGaugeSpeed"] = Int((b, n) => b.DefaultGaugeSpeed = n),
        ["SideEffectProbability"] = Int((b, n) => b.SideEffectProbability = n),
        ["CanBeCumulated"] = (b, v, _) => { if (v == "true") b.CanBeCumulated = 1; },
        ["GreateSuccessBuff"] = (b, _, line) => b.GreateSuccessBuff = ParseChoice(line),
        ["SuccessBuff"] = (b, _, line) => b.SuccessBuff = ParseChoice(line),
        ["HandicapBuff"] = (b, _, line) => b.HandicapBuff = ParseChoice(line),
        ["SideEffectBuff"] = (b, _, line) => b.SideEffectBuff = ParseChoice(line),
    };

    private static FieldReader Int(Action<Pending, int> set) =>
        (b, v, _) => { if (int.TryParse(v, out int n)) set(b, n); };

    // (nBuffID=..,byGoalSizeRatio=..,byMaxBuffLevel=..)
    private static Choice ParseChoice(string line) => new(TupleInt(line, "nBuffID=", 0),
        checked((byte)TupleInt(line, "byGoalSizeRatio=", 10)),
        checked((byte)TupleInt(line, "byMaxBuffLevel=", 1)));

    private static int TupleInt(string line, string key, int fallback)
    {
        int start = line.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return fallback;
        start += key.Length;
        int end = start;
        while (end < line.Length && char.IsDigit(line[end])) end++;
        return int.TryParse(line[start..end], out int value) ? value : fallback;
    }
}
