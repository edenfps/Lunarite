using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

public sealed class CraftCatalog
{
    private readonly ILogger _logger;

    public sealed record Material(int ItemId, int Count);

    public sealed record Recipe(int EntryIndex, IReadOnlyList<Material> Materials, int CraftCost,
        int MakeItemIndex, int ChainLearnIndex, double ChainLearnRate, double LuckyRate, int LuckyItemIndex);

    private readonly Dictionary<int, Recipe> _recipes = [];
    private readonly object _lock = new();
    private bool _loaded;

    public CraftCatalog(ILogger<CraftCatalog> logger)
    {
        _logger = logger;
    }

    public Recipe? Get(int id) { EnsureLoaded(); return _recipes.GetValueOrDefault(id); }

    public IReadOnlyCollection<Recipe> All { get { EnsureLoaded(); return _recipes.Values; } }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            string? path = FindDump();
            if (path == null) { _logger.LogError("[CraftCatalog] HuxleyItemTable-craft.txt not found."); return; }
            Load(path);
            _logger.LogInformation("[CraftCatalog] loaded {0} retail recipes.", _recipes.Count);
        }
    }

    private static string? FindDump()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string path = Path.Combine(dir, "data", "HuxleyItemTable-craft.txt");
            if (File.Exists(path)) return path;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    private void Load(string path)
    {
        Pending? recipe = null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
            {
                recipe = line.Contains("class=HuxleyGame.HuxleyCityItemData_CraftRecipe", StringComparison.Ordinal)
                    ? new Pending() : null;
                continue;
            }
            if (recipe == null) continue;
            if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
            {
                if (recipe.EntryIndex > 0 && recipe.MakeItemIndex > 0)
                    _recipes[recipe.EntryIndex] = recipe.Build();
                recipe = null;
                continue;
            }
            int split = line.IndexOfAny(['=', '[']);
            if (split > 0 && Fields.TryGetValue(line[..split], out var read))
                read(recipe, line[(line.IndexOf('=') + 1)..], line);
        }
    }

    // One HuxleyCityItemData_CraftRecipe object while it is being read.
    private sealed class Pending
    {
        public int EntryIndex, CraftCost, MakeItemIndex, ChainLearnIndex, LuckyItemIndex;
        public double ChainLearnRate, LuckyRate;
        public List<Material> CraftMaterials = [];

        public Recipe Build() => new(EntryIndex, CraftMaterials.ToArray(), CraftCost, MakeItemIndex,
            ChainLearnIndex, ChainLearnRate, LuckyRate, LuckyItemIndex);
    }

    private delegate void FieldReader(Pending recipe, string value, string line);

    private static readonly Regex MaterialRx = new(
        @"^CraftMaterials\[\d+\]=\(MaterialIndex=(\d+),MaterialCount=(\d+)\)$");

    // Exported property name (the text before '=' or '[') -> how a recipe reads it.
    private static readonly Dictionary<string, FieldReader> Fields = new(StringComparer.Ordinal)
    {
        ["EntryIndex"] = Int((r, n) => r.EntryIndex = n),
        ["CraftCost"] = Int((r, n) => r.CraftCost = n),
        ["MakeItemIndex"] = Int((r, n) => r.MakeItemIndex = n),
        ["ChainLearnIndex"] = Int((r, n) => r.ChainLearnIndex = n),
        ["LuckyItemIndex"] = Int((r, n) => r.LuckyItemIndex = n),
        ["ChainLearnRate"] = Double((r, d) => r.ChainLearnRate = d),
        ["LuckyRate"] = Double((r, d) => r.LuckyRate = d),
        ["CraftMaterials"] = (r, _, line) =>
        {
            var match = MaterialRx.Match(line);
            if (match.Success)
                r.CraftMaterials.Add(new(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)));
        },
    };

    private static FieldReader Int(Action<Pending, int> set) =>
        (r, v, _) => { if (int.TryParse(v, out int n)) set(r, n); };

    private static FieldReader Double(Action<Pending, double> set) =>
        (r, v, _) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) set(r, d); };
}
