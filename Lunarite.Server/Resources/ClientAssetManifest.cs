using System;
using System.Collections.Generic;
using System.Linq;

namespace Lunarite.Resources;

// Exact export names observed in supplied client packages. This is deliberately separate from
// gameplay/item IDs: UE3 DynamicLoadObject resolves package.object paths, not semantic IDs.
public static class ClientAssetManifest
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Exports =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["TextureIngameShop"] = Enumerable.Range(1, 153)
                .Select(index => $"InGameShopItem_{index:00}")
                .Append("InGameShopItem_26_C")
                .Append("dummyIcon")
                .Append("ShopIcons")
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

    public static bool Contains(string packageName, string exportName) =>
        Exports.TryGetValue(packageName, out HashSet<string>? exports) && exports.Contains(exportName);

    public static bool ContainsPath(string objectPath)
    {
        int separator = objectPath.IndexOf('.');
        return separator > 0 && separator < objectPath.Length - 1
            && Contains(objectPath[..separator], objectPath[(separator + 1)..]);
    }

    public static string Path(string packageName, string exportName)
    {
        if (!Contains(packageName, exportName))
            throw new ArgumentException($"Unresolved client asset: {packageName}.{exportName}");
        return $"{packageName}.{exportName}";
    }
}
