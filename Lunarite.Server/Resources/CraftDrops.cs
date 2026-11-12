using System;
using System.Collections.Generic;
using System.Linq;

namespace Lunarite.Resources;

// Crafting materials and schematics from quest enemies (analysis/audit/crafting.md, option A; user 2026-11-12).
// Retail data has no source for 77 of the 79 materials and sells 16 of the 158 schematics, so crafting could not be
// used. Every enemy of level 10+ now also rolls, by its level band (B 10-19, A 20-29, S 30+):
// - MaterialChance for one material of that tier, picked by weight: the square root of how many of it the tier's
//   recipes use in total, so common parts drop more without the rare ones becoming unreachable. Structural parts
//   (rod, plate, pipe, plastic: ids ending 005-008) drop 3 at a time, as recipes use them by the dozen.
// - SchematicChance for one schematic of that tier that no vendor sells. The duplicate Iceblender schematic (616300)
//   and the two Lunarite test recipes (611108, 611109) are left out.
// Server only: the client spawns any item id it is sent in op59.
public sealed class CraftDrops
{
    public const double MaterialChance = 200_000;  // 20 % per kill, out of QuestLoot's 1,000,000
    public const double SchematicChance = 10_000;  // 1 % per kill
    private static readonly HashSet<int> LeftOut = [616300, 611108, 611109];

    public sealed record Tier(char Name, IReadOnlyList<(int Item, double Weight)> Materials, IReadOnlyList<int> Schematics);

    private readonly CraftCatalog _craftCatalog;
    private readonly ItemGroupCatalog _itemGroupCatalog;
    private readonly Lazy<Dictionary<char, Tier>> _tiers;

    public CraftDrops(CraftCatalog craftCatalog, ItemGroupCatalog itemGroupCatalog)
    {
        _craftCatalog = craftCatalog;
        _itemGroupCatalog = itemGroupCatalog;
        _tiers = new(Build);
    }

    // Recipe and material ids: 61xxxx = B, 62xxxx = A, 63xxxx = S.
    public static char TierOf(int id) => (id / 10000) switch { 61 => 'B', 62 => 'A', 63 => 'S', _ => '?' };

    public static char TierForLevel(int enemyLevel) => enemyLevel switch { >= 30 => 'S', >= 20 => 'A', >= 10 => 'B', _ => '?' };

    public static int QuantityOf(int material) => material % 1000 is >= 5 and <= 8 ? 3 : 1;

    public Tier? ForTier(char tier) => _tiers.Value.GetValueOrDefault(tier);

    private Dictionary<char, Tier> Build()
    {
        var result = new Dictionary<char, Tier>();
        var recipes = _craftCatalog.All.Where(r => !LeftOut.Contains(r.EntryIndex)).ToList();
        foreach (char tier in "BAS")
        {
            var ofTier = recipes.Where(r => TierOf(r.EntryIndex) == tier).ToList();
            var demand = new Dictionary<int, int>();
            foreach (var material in ofTier.SelectMany(r => r.Materials).Where(m => m.ItemId > 0 && m.Count > 0))
                demand[material.ItemId] = demand.GetValueOrDefault(material.ItemId) + material.Count;
            var materials = demand.OrderBy(pair => pair.Key)
                .Select(pair => (pair.Key, Math.Sqrt(pair.Value / (double)QuantityOf(pair.Key)))).ToList();
            var schematics = ofTier.Select(r => r.EntryIndex).Where(id => !_itemGroupCatalog.IsVendorItem(id))
                .Order().ToList();
            result[tier] = new Tier(tier, materials, schematics);
        }
        return result;
    }

    // What a kill of an enemy of this level adds, with chance hit test supplied by QuestLoot.
    public IEnumerable<(int item, int num)> Roll(int enemyLevel, Func<double, bool> hit)
    {
        if (ForTier(TierForLevel(enemyLevel)) is not { } tier) yield break;
        if (tier.Materials.Count > 0 && hit(MaterialChance))
        {
            double pick = Random.Shared.NextDouble() * tier.Materials.Sum(m => m.Weight);
            int chosen = tier.Materials.Count - 1;
            for (int i = 0; i < tier.Materials.Count; i++)
            {
                pick -= tier.Materials[i].Weight;
                if (pick <= 0) { chosen = i; break; }
            }
            yield return (tier.Materials[chosen].Item, QuantityOf(tier.Materials[chosen].Item));
        }
        if (tier.Schematics.Count > 0 && hit(SchematicChance))
            yield return (tier.Schematics[Random.Shared.Next(tier.Schematics.Count)], 1);
    }

    public bool CanDrop(int enemyLevel, int itemId) =>
        ForTier(TierForLevel(enemyLevel)) is { } tier
        && (tier.Materials.Any(m => m.Item == itemId) || tier.Schematics.Contains(itemId));
}
