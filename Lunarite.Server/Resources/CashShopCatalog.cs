using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Lunarite.Resources;

// Private-server replacement for Webzen's lost ProductTransfer catalog. Item membership and item
// IDs come from the cooked HuxleyItemTable.PayItem table. Category placement, display names and
// package identifiers are intentionally local metadata; every package is free.
public sealed class CashShopCatalog
{
    private readonly ItemCatalog _itemCatalog;

    // The in-game shop is deferred: op197 shop-open, Buy and Present answer their fail codes, so
    // the free catalog cannot mint items (HPointCoupons alone are worth up to 2,000,000 QT each).
    // Paid storage still lists and releases what GM !givepaid puts there.
    public static bool ShopOpen = false;

    public enum ExecutionRoute : byte
    {
        Unsupported,
        UseItem,
        ResetTuneUp,
        ResetBind
    }

    public sealed record ExecutionCoverage(int Products,
        IReadOnlyDictionary<ExecutionRoute, int> Routes, IReadOnlyList<int> UnsupportedItems);

    public sealed record Entry(int ItemId, int PackageId, int CategoryId, string Name,
        string Description, string IconName);

    private sealed record Category(int RootId, int ChildId, string Name);

    private static readonly Category[] Categories =
    [
        new(101, 201, "Travel"),
        new(102, 202, "Licenses"),
        new(103, 203, "Character Services"),
        new(104, 204, "Boosters"),
        new(105, 205, "Other")
    ];

    public CashShopCatalog(ItemCatalog itemCatalog)
    {
        _itemCatalog = itemCatalog;
    }

    public IReadOnlyList<Entry> Entries => _itemCatalog.PayItems.Select((item, index) =>
    {
        Category category = CategoryFor(item.ClassName);
        string name = ItemLocalization.Get(item.EntryIndex, "name")
            ?? FriendlyName(item.ClassName, item.EntryIndex);
        string description = ItemLocalization.Get(item.EntryIndex, "summary")
            ?? ItemLocalization.Get(item.EntryIndex, "description")
            ?? $"Item {item.EntryIndex}.";
        return new Entry(item.EntryIndex, 1_000_000 + item.EntryIndex, category.ChildId,
            name, description, IconFor(item));
    }).ToArray();

    public Entry? FindPackage(int packageId) => Entries.FirstOrDefault(x => x.PackageId == packageId);

    // A listed product is useful only when the corresponding inventory item has an authoritative
    // transaction. Most products use op77; tune-up and bind resetters are consumed through their
    // dedicated target-selection menus. Keep this exact-class audit beside catalog generation so
    // a future cooked table cannot silently add a free but inert storefront product.
    public static ExecutionRoute RouteFor(ItemCatalog.Item item) => item.ClassName switch
    {
        "HuxleyCityItemData_CityWarpPass" or
        "HuxleyCityItemData_EnlargeBank" or
        "HuxleyCityItemData_EnlargeInventory" or
        "HuxleyCityItemData_HPointCoupon" or
        "HuxleyCityItemData_IncreaseContamination" or
        "HuxleyCityItemData_OpenWeaponSlot" or
        "HuxleyCityItemData_PersonalRespawn" or
        "HuxleyCityItemData_ResetBattleRecord" or
        "HuxleyCityItemData_TermLicense" => ExecutionRoute.UseItem,
        "HuxleyCityItemData_ResetTuneup" => ExecutionRoute.ResetTuneUp,
        "HuxleyCityItemData_RemoveItemBind" => ExecutionRoute.ResetBind,
        _ => ExecutionRoute.Unsupported
    };

    public ExecutionCoverage AuditExecutionCoverage()
    {
        var items = _itemCatalog.PayItems;
        var routes = items.GroupBy(RouteFor).ToDictionary(group => group.Key, group => group.Count());
        var unsupported = items.Where(item => RouteFor(item) == ExecutionRoute.Unsupported)
            .Select(item => item.EntryIndex).Order().ToArray();
        return new ExecutionCoverage(items.Count, routes, unsupported);
    }

    public static string CategoryFile()
    {
        // Native LoadShopScript partitions the hierarchy by its hidden root sequence:
        // 228 is the GCoin/cash shop. Displayed categories are its children, and the
        // package-bearing tabs are their children. A two-level hierarchy parses but
        // leaves GCoinShopCategories empty.
        var rows = new List<string>
        {
            "228@Free Shop@200@201@228@1@1"
        };
        int order = 1;
        foreach (Category category in Categories)
        {
            // Native IGS_Category parser: seq@name@event@open@parent@order@root.
            rows.Add($"{category.RootId}@{category.Name}@200@201@228@{order}@0");
            rows.Add($"{category.ChildId}@All {category.Name}@200@201@{category.RootId}@1@0");
            order++;
        }
        return string.Join("\r\n", rows) + "\r\n";
    }

    public string ProductFile() => string.Join("\r\n", Entries.Select(entry =>
        // Native IGS_Product parser: 5 strings + 8 ints + string + 3 ints.
        $"{entry.ItemId}@{Clean(entry.Name)}@Duration@0@@0@{entry.ItemId}@142@146@1@144@0@0@{entry.ItemId}@0@170@0")) + "\r\n";

    public string PackageFile() => string.Join("\r\n", Entries.Select((entry, index) =>
        // Native IGS_Package parser. Product/price arrays are pipe-delimited fields 20 and 24.
        $"{entry.CategoryId}@{index + 1}@{entry.PackageId}@{Clean(entry.Name)}@170@0@{Clean(entry.Description)}@@182@184@20000101000000@20991231235959@177@1@{Clean(entry.Name)}@Free@181@200@1@{entry.ItemId}@{entry.IconName}@0@1@{entry.ItemId}@0")) + "\r\n";

    public string IndexHtml()
    {
        var rows = new StringBuilder();
        foreach (Entry entry in Entries)
            rows.Append("<tr><td>").Append(entry.ItemId).Append("</td><td>")
                .Append(WebUtility.HtmlEncode(entry.Name)).Append("</td><td>Free</td></tr>");
        return "<!doctype html><meta charset=utf-8><title>Huxley Private Shop</title>"
            + "<style>body{font:16px sans-serif;background:#111;color:#ddd;max-width:900px;margin:2rem auto}"
            + "table{width:100%;border-collapse:collapse}td,th{padding:.5rem;border-bottom:1px solid #444}</style>"
            + "<h1>Huxley Private Shop</h1><p>Cooked pay-item catalog. All packages are free.</p>"
            + "<table><tr><th>Item ID</th><th>Item</th><th>Price</th></tr>" + rows + "</table>";
    }

    private static Category CategoryFor(string className)
    {
        if (className.Contains("Warp", StringComparison.OrdinalIgnoreCase)) return Categories[0];
        if (className.Contains("License", StringComparison.OrdinalIgnoreCase)) return Categories[1];
        if (className.Contains("Enlarge", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Reset", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Remove", StringComparison.OrdinalIgnoreCase)
            || className.Contains("OpenWeaponSlot", StringComparison.OrdinalIgnoreCase)) return Categories[2];
        if (className.Contains("HPoint", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Contamination", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Respawn", StringComparison.OrdinalIgnoreCase)) return Categories[3];
        return Categories[4];
    }

    private static string FriendlyName(string className, int itemId)
    {
        string name = className;
        int marker = name.LastIndexOf("Data_", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) name = name[(marker + 5)..];
        name = name.Replace('_', ' ').Trim();
        return name.Length == 0 ? $"Pay Item {itemId}" : name;
    }

    private static string IconFor(ItemCatalog.Item item)
    {
        // Compatibility mapping to exports proven present in TextureIngameShop.upk. Original
        // ProductTransfer package-to-icon rows are lost; keep this isolated so exact retail rows
        // can replace it when recovered without changing product/item resolution.
        string icon = item.ClassName switch
        {
            "HuxleyCityItemData_CityWarpPass" => "InGameShopItem_60",
            "HuxleyCityItemData_EnlargeBank" => "InGameShopItem_49",
            "HuxleyCityItemData_EnlargeInventory" => "InGameShopItem_48",
            "HuxleyCityItemData_HPointCoupon" => "InGameShopItem_59",
            "HuxleyCityItemData_IncreaseContamination" => item.GeneralValue >= 5
                ? "InGameShopItem_51" : "InGameShopItem_50",
            "HuxleyCityItemData_OpenWeaponSlot" => item.GeneralValue >= 3
                ? "InGameShopItem_55" : "InGameShopItem_54",
            "HuxleyCityItemData_PersonalRespawn" => item.GeneralValue >= 10
                ? "InGameShopItem_47" : "InGameShopItem_46",
            "HuxleyCityItemData_RemoveItemBind" => "InGameShopItem_57",
            "HuxleyCityItemData_ResetBattleRecord" => "InGameShopItem_56",
            "HuxleyCityItemData_ResetTuneup" => item.ResetTuneupAll
                ? "InGameShopItem_52" : "InGameShopItem_53",
            "HuxleyCityItemData_TermLicense" => "InGameShopItem_58",
            _ => "dummyIcon"
        };
        _ = ClientAssetManifest.Path("TextureIngameShop", icon);
        return icon;
    }

    private static string Clean(string value) => value.Replace('@', ' ').Replace('|', ' ')
        .Replace("\r", " ").Replace("\n", " ");
}
