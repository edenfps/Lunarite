using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

// HuxleyItemTable.ItemGroups is the source used by the exported city trade menus.
// The buy wire request has no NPC/group field, so this establishes only that an item
// appears in a group referenced by a cooked city OpenTradeMenu, not which NPC opened it.
public sealed class ItemGroupCatalog
{
    private readonly ILogger _logger;

    // Exhaustive 28-package US-client city *Script*.war Kismet audit (2026-09-23).
    // Other cooked groups include initial loadouts, retired lists, and training data;
    // their mere presence in ItemGroups does not make them purchasable at a city vendor.
    private static readonly HashSet<string> CityVendorGroups = new(StringComparer.Ordinal)
    {
        "Ticket_T", "BI_CB", "A_AS_H", "A_S_H", "A_AS_M", "A_AS_L", "W_CB",
        "A_S_M", "A_S_L", "Trans_Ticket", "A_CB_M", "SK_Me_CB", "A_CB_L",
        "A_CB_H", "BI_BA", "W_BA", "BI_AS", "SK_Me_BA", "SK_Me_AS",
        "Vehicle_T", "RCP_BA", "A_BA_L", "W_AS", "A_BA_H", "A_BA_M", "W_S"
    };
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<int>>> Groups;
    private readonly Lazy<HashSet<int>> VendorItems;

    public ItemGroupCatalog(ILogger<ItemGroupCatalog> logger)
    {
        _logger = logger;
        Groups = new(Load);
        VendorItems = new(() =>
        Groups.Value.Where(group => CityVendorGroups.Contains(group.Key))
            .SelectMany(group => group.Value).ToHashSet());
    }

    public IReadOnlyDictionary<string, IReadOnlyList<int>> All => Groups.Value;

    public static int CityVendorGroupCount => CityVendorGroups.Count;

    public static IReadOnlySet<string> CityVendorGroupNames => CityVendorGroups;

    public bool IsVendorItem(int itemId) => VendorItems.Value.Contains(itemId);

    private IReadOnlyDictionary<string, IReadOnlyList<int>> Load()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string path = Path.Combine(dir, "data", "HuxleyItemGroups.tsv");
            if (File.Exists(path)) return Parse(File.ReadLines(path));
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        _logger.LogError("[ItemGroupCatalog] cooked vendor-group export missing; vendor purchases are disabled.");
        return new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<int>> Parse(IEnumerable<string> lines)
    {
        var groups = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t', 2);
            if (fields.Length != 2 || fields[0].Length == 0)
                throw new InvalidDataException("Malformed cooked item-group row.");
            int[] items = fields[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse).ToArray();
            if (items.Length == 0 || !groups.TryAdd(fields[0], items))
                throw new InvalidDataException($"Empty or duplicate cooked item group: {fields[0]}");
        }
        if (groups.Count == 0) throw new InvalidDataException("Cooked item-group export is empty.");
        return groups;
    }
}
