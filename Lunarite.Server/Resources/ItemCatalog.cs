using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

// Retail item economics and stack limits, read directly from the exported UE3 data-table objects.
// Most EntryIndex values are full wire item IDs. Normal-table children use their
// parent HuxleyDataTable.MasterIndex as an offset.
public sealed class ItemCatalog
{
    private readonly ILogger _logger;

    public sealed record ExchangeCost(int ItemId, int Count);

    public sealed record Item(int EntryIndex, int Price, int SellPrice, int MaxCollapseCount,
        int EquipLevel, byte EquipRank, int ItemGrade, bool Repairable, bool Vehicle, bool Enabled,
        int GeneralValue, int TuneupFee,
        int TuneupLunaritesA, int TuneupLunaritesB, int TuneupLunaritesC,
        bool ResetTuneupAll, bool ResetTuneupPart, bool RemoveItemBind, bool BindOnEquip, bool BindOnPickup,
        string ClassName, string CombatStyle, string ExclusiveCombatStyle, string WeaponGroup, string ArmorPart,
        int AuctionCategory,
        int ItemLimitTimeType, int ItemLimitDuration, int CityItemDuration, byte CityItemGroup,
        int FixedLimitTime, int ItemSalesCategory, bool CanBeOverlapped, bool PermitSell,
        bool OnlyForSuperUser, bool OnlyQtItem, int QtPrice, int AdditionalQt,
        IReadOnlyList<ExchangeCost> ExchangeItems,
        IReadOnlyList<int> TuneupOptionCaps, string SkillItemShape, IReadOnlyList<string> SkillSocketShapes,
        bool IsPayItem);

    // One item object while it is being read. Fields named after a UE property hold that
    // property; the rest are derived from the object's class or reference line.
    private sealed class Pending
    {
        public int EntryIndex, Price, SellPrice, MaxCollapseCount = 1, EquipLevel, GeneralValue;
        public byte EquipRank;
        public int ItemGrade;
        public bool Enabled = true;
        public int TuneupFee, TuneupLunaritesA, TuneupLunaritesB, TuneupLunaritesC;
        public bool HasTuneupFee, HasTuneupLunaritesA; // line present, even when 0
        public int ItemLimitTimeTypeCode, ItemLimitDuration, CityItemDuration, LimitDate, ItemSalesCategory;
        public byte CityItemGroupCode;
        public bool CanBeOverlapped, PermitSell = true;
        public bool OnlyForSuperUser, OnlyQTItem;
        public int QTPrice, AdditionalQT;
        public List<ExchangeCost> ExchangeItems = [];
        public string CombatStyle = "", ExclusiveCombatStyle = "", WeaponGroup = "", ItemPos = "";
        // TuneUpCategories[n].MaxTuneUps, indexed by n.
        public List<int> TuneUpCategories = [];
        public string SkillItemShape = "SkillItemShape_Minus";
        public List<string> SkillSockets = [];
        public bool BindOnEquip, BindOnPickup; // ItemBinds
        public bool ResetTuneupAll, ResetTuneupPart; // class + ResetAll
        public string ClassName = "";
        public bool Repairable, Vehicle, RemoveItemBind, IsPayItem;
    }

    private readonly object _lock = new();
    private readonly Dictionary<int, Item> _items = [];
    private bool _loaded;

    public ItemCatalog(ILogger<ItemCatalog> logger)
    {
        _logger = logger;
    }

    public Item? Get(int itemId)
    {
        EnsureLoaded();
        return _items.GetValueOrDefault(itemId);
    }

    public IReadOnlyList<Item> All()
    {
        EnsureLoaded();
        lock (_lock) return _items.Values.ToArray();
    }

    // Collected quest items (603000-603999) carry a sell price but bPermitSell=false, and nothing takes them at
    // turn-in, so they piled up as junk. They can be sold (decided 2026-11-10); the client gets the same flag from
    // LunariteMenuGame. Items a quest hands out (its own index, RemoveItems) are not in this range.
    public static bool IsSellableQuestPickup(int itemId, int sellPrice) =>
        itemId is >= 603000 and <= 603999 && sellPrice > 0;

    public IReadOnlyList<Item> PayItems
    {
        get
        {
            EnsureLoaded();
            lock (_lock) return _items.Values.Where(x => x.IsPayItem).OrderBy(x => x.EntryIndex).ToArray();
        }
    }

    public int DisabledCount
    {
        get { EnsureLoaded(); lock (_lock) return _items.Values.Count(x => !x.Enabled); }
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            string? tables = FindTables();
            if (tables == null)
            {
                _logger.LogError("[ItemCatalog] exported table directory not found; vendor transactions are disabled.");
                return;
            }
            foreach (string name in new[]
            {
                "HuxleyItemTable.txt", "HuxleyWeaponTable.txt", "HuxleySkillItemTable.txt", "HuxleyItemNormalTable.txt"
            })
            {
                string path = Path.Combine(tables, name);
                if (File.Exists(path)) Load(path);
            }
            _logger.LogInformation("[ItemCatalog] loaded {0} retail item definitions.", _items.Count);
        }
    }

    private static string? FindTables()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "data", "tables");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    private void Load(string path)
    {
        var stack = new Stack<Pending>();
        int masterIndex = 0;
        bool nextObjectIsPayItem = false;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            // HuxleyItemNormalTable stores child EntryIndex values relative to its MasterIndex.
            if (TryInt(line, "MasterIndex=", out int tableIndex))
            {
                masterIndex = tableIndex;
                continue;
            }
            if (line.StartsWith("// Reference:", StringComparison.Ordinal))
            {
                nextObjectIsPayItem = line.Contains("'HuxleyItemTable.PayItem.", StringComparison.Ordinal);
                continue;
            }
            if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
            {
                bool resetTuneup = line.Contains("HuxleyCityItemData_ResetTuneup", StringComparison.Ordinal);
                stack.Push(new Pending
                {
                    ClassName = ClassName(line),
                    // HuxleyMMOItemData_Helmet/Upbody inherit MultiParts -> HuxleyArmorData;
                    // base HuxleyWeaponData and HuxleyRapidWeaponData are weapons too.
                    Repairable = IsRepairableClass(ClassName(line)),
                    Vehicle = line.Contains("HuxleyCityPersonalVehicleData", StringComparison.Ordinal),
                    ResetTuneupAll = resetTuneup,
                    RemoveItemBind = line.Contains("HuxleyCityItemData_RemoveItemBind", StringComparison.Ordinal),
                    IsPayItem = nextObjectIsPayItem
                });
                nextObjectIsPayItem = false;
                continue;
            }
            if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count == 0) continue;
                var pending = stack.Pop();
                if (pending.EntryIndex > 0)
                {
                    if (pending.WeaponGroup.Length == 0) pending.WeaponGroup = DefaultWeaponGroup(pending.ClassName);
                    if (pending.ExclusiveCombatStyle.Length == 0)
                        pending.ExclusiveCombatStyle = DefaultExclusiveCombatStyle(pending.ClassName);
                    if (pending.ItemPos.Length == 0) pending.ItemPos = DefaultItemPos(pending.ClassName);
                    if (IsArmorClass(pending.ClassName)) ApplyArmorTuneUpDefaults(pending);
                    if (IsArmorClass(pending.ClassName) || IsWeaponClass(pending.ClassName))
                        ApplyTuneUpCostDefaults(pending);
                    if (IsSellableQuestPickup(pending.EntryIndex, pending.SellPrice)) pending.PermitSell = true;
                    var item = new Item(pending.EntryIndex, pending.Price, pending.SellPrice,
                        Math.Max(1, pending.MaxCollapseCount), pending.EquipLevel, pending.EquipRank, pending.ItemGrade,
                        pending.Repairable, pending.Vehicle,
                        pending.Enabled,
                        pending.GeneralValue,
                        pending.TuneupFee, pending.TuneupLunaritesA, pending.TuneupLunaritesB, pending.TuneupLunaritesC,
                        pending.ResetTuneupAll, pending.ResetTuneupPart, pending.RemoveItemBind, pending.BindOnEquip,
                        pending.BindOnPickup,
                        pending.ClassName, pending.CombatStyle, pending.ExclusiveCombatStyle, pending.WeaponGroup,
                        pending.ItemPos,
                        AuctionCategory(pending.ClassName, pending.CombatStyle),
                        pending.ItemLimitTimeTypeCode, pending.ItemLimitDuration, pending.CityItemDuration,
                        pending.CityItemGroupCode,
                        pending.LimitDate, pending.ItemSalesCategory, pending.CanBeOverlapped, pending.PermitSell,
                        pending.OnlyForSuperUser, pending.OnlyQTItem, pending.QTPrice, pending.AdditionalQT,
                        pending.ExchangeItems.ToArray(),
                        pending.TuneUpCategories.ToArray(), pending.SkillItemShape, pending.SkillSockets.ToArray(),
                        pending.IsPayItem);
                    // The weapon dump contains later generic merged duplicates of specific records.
                    // Preserve the specific class/category while still accepting otherwise newer data.
                    if (!_items.TryGetValue(pending.EntryIndex, out var existing)
                        || existing.AuctionCategory == 26 || item.AuctionCategory != 26)
                        _items[pending.EntryIndex] = item;
                }
                continue;
            }
            if (stack.Count == 0) continue;
            var current = stack.Peek();
            int split = line.IndexOfAny(['=', '[']);
            if (split <= 0) continue;
            string key = line[..split];
            string value = line[(split + 1)..];
            if (key == "EntryIndex")
            {
                if (int.TryParse(value, out int entry)) current.EntryIndex = checked(masterIndex + entry);
            }
            else if (Fields.TryGetValue(key, out var read)) read(current, value, line);
        }
    }

    private delegate void FieldReader(Pending item, string value, string line);

    // Exported property name (the text before '=' or '[') -> how a record reads it. Properties not
    // listed here are ignored. Array lines (SkillSockets[n], ExchangeItems[n], TuneUpCategories[n])
    // pass the whole line, whose element syntax the helpers below parse.
    private static readonly Dictionary<string, FieldReader> Fields = new(StringComparer.Ordinal)
    {
        ["Price"] = Int((p, v) => p.Price = v),
        ["SellPrice"] = Int((p, v) => p.SellPrice = v),
        ["MaxCollapseCount"] = Int((p, v) => p.MaxCollapseCount = v),
        ["EquipLevel"] = Int((p, v) => p.EquipLevel = v),
        ["GeneralValue"] = Int((p, v) => p.GeneralValue = v),
        ["EquipRank"] = (p, v, _) => p.EquipRank = ParseRank(v),
        ["CombatStyle"] = (p, v, _) => p.CombatStyle = v,
        ["ExclusiveCombatStyle"] = (p, v, _) => p.ExclusiveCombatStyle = v,
        ["WeaponGroup"] = (p, v, _) => p.WeaponGroup = v,
        ["ItemPos"] = (p, v, _) => p.ItemPos = v,
        ["SkillItemShape"] = (p, v, _) => p.SkillItemShape = v,
        ["SkillSockets"] = (p, _, line) => p.SkillSockets.Add(SocketShape(line)),
        ["ItemBinds"] = (p, v, _) =>
        {
            if (v == "Binds_Equipped") p.BindOnEquip = true;
            else if (v == "Binds_PickedUp") p.BindOnPickup = true;
        },
        ["TuneupFee"] = Int((p, v) => { p.TuneupFee = v; p.HasTuneupFee = true; }),
        ["TuneupLunaritesA"] = Int((p, v) => { p.TuneupLunaritesA = v; p.HasTuneupLunaritesA = true; }),
        ["TuneupLunaritesB"] = Int((p, v) => p.TuneupLunaritesB = v),
        ["TuneupLunaritesC"] = Int((p, v) => p.TuneupLunaritesC = v),
        ["ItemLimitTimeTypeCode"] = Int((p, v) => p.ItemLimitTimeTypeCode = v),
        ["ItemLimitDuration"] = Int((p, v) => p.ItemLimitDuration = v),
        ["CityItemDuration"] = Int((p, v) => p.CityItemDuration = v),
        ["CityItemGroupCode"] = Int((p, v) => p.CityItemGroupCode = checked((byte)v)),
        ["LimitDate"] = (p, _, line) => { if (TryLimitDate(line, out int time)) p.LimitDate = time; },
        ["bOnlyForSuperUser"] = (p, v, _) => { if (v == "true") p.OnlyForSuperUser = true; },
        ["bEnabled"] = (p, v, _) => { if (v == "false") p.Enabled = false; },
        ["bOnlyQTItem"] = (p, v, _) => { if (v == "true") p.OnlyQTItem = true; },
        ["QTPrice"] = Int((p, v) => p.QTPrice = v),
        ["AdditionalQT"] = Int((p, v) => p.AdditionalQT = v),
        ["ItemSalesCategory"] = (p, v, _) => p.ItemSalesCategory = v switch
        {
            "EISC_PCRoom" => 1,
            "EISC_Event" => 2,
            _ => 0
        },
        ["bCanBeOverlapped"] = (p, v, _) => { if (v == "true") p.CanBeOverlapped = true; },
        ["bPermitSell"] = (p, v, _) => { if (v == "false") p.PermitSell = false; },
        ["ExchangeItems"] = (p, _, line) => { if (TryExchangeCost(line, out var cost)) p.ExchangeItems.Add(cost); },
        ["TuneUpCategories"] = (p, _, line) =>
        {
            if (!TryTuneupCap(line, out int optionIndex, out int cap)) return;
            while (p.TuneUpCategories.Count <= optionIndex) p.TuneUpCategories.Add(0);
            p.TuneUpCategories[optionIndex] = cap;
        },
        ["ResetAll"] = (p, v, _) =>
        {
            if (v == "false" && p.ResetTuneupAll) { p.ResetTuneupAll = false; p.ResetTuneupPart = true; }
        },
        ["ItemGrade"] = (p, v, _) =>
        {
            if (!v.StartsWith("Grade_", StringComparison.Ordinal)) return;
            p.ItemGrade = v switch
            {
                "Grade_Worthless" => 0, "Grade_Plentiful" => 1, "Grade_Common" => 2,
                "Grade_Worth" => 3, "Grade_Uncommon" => 4, "Grade_Precious" => 5,
                "Grade_Peerless" => 6, "Grade_Relic" => 7, _ => 8
            };
        },
    };

    private static FieldReader Int(Action<Pending, int> set) =>
        (p, v, _) => { if (int.TryParse(v, out int n)) set(p, n); };

    private static bool TryLimitDate(string line, out int unixSeconds)
    {
        unixSeconds = 0;
        if (!line.StartsWith("LimitDate=(", StringComparison.Ordinal)) return false;
        int Part(string name, int fallback = 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, name + "=\"([^\"]*)\"");
            return match.Success && int.TryParse(match.Groups[1].Value.Split(':')[0], out int value)
                ? value : fallback;
        }
        int year = Part("Year");
        int month = Part("Month", 1);
        int day = Part("Day", 1);
        int hour = Part("Hour");
        if (year <= 0) return true;
        try { unixSeconds = checked((int)new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()); }
        catch (ArgumentOutOfRangeException) { unixSeconds = 0; }
        return true;
    }

    private static bool IsRepairableClass(string className) =>
        className.StartsWith("HuxleyMMOItemData_", StringComparison.Ordinal)
        || className is "HuxleyArmorData" or "HuxleyWeaponData" or "HuxleyRapidWeaponData"
        || className.StartsWith("HuxleyWeaponData_", StringComparison.Ordinal);

    private static string ClassName(string line)
    {
        const string marker = "class=HuxleyGame.";
        int start = line.IndexOf(marker, StringComparison.Ordinal);
        return start < 0 ? "" : line[(start + marker.Length)..].Trim();
    }

    private static int AuctionCategory(string className, string combatStyle)
    {
        string[] weapons = ["ShotGun", "Flinger", "Discharger", "MachineGun", "RocketLauncher",
            "Optical", "Sniper", "Regulator", "Inquisitor"];
        for (int i = 0; i < weapons.Length; i++)
            if (className.Contains(weapons[i], StringComparison.OrdinalIgnoreCase)) return i;

        int part = className switch
        {
            var x when x.Contains("Helmet", StringComparison.OrdinalIgnoreCase) => 0,
            var x when x.Contains("Upbody", StringComparison.OrdinalIgnoreCase) => 1,
            var x when x.Contains("Lowbody", StringComparison.OrdinalIgnoreCase) => 2,
            var x when x.Contains("Gloves", StringComparison.OrdinalIgnoreCase) => 3,
            var x when x.Contains("Boots", StringComparison.OrdinalIgnoreCase) => 4,
            _ => -1
        };
        if (part >= 0)
        {
            int armor = combatStyle switch
            {
                "CombatStyle_Enforcer" => 9,
                "CombatStyle_Avenger" => 14,
                "CombatStyle_Phantom" => 19,
                _ => -1
            };
            if (armor >= 0) return armor + part;
        }
        if (className.Contains("SkillData_", StringComparison.Ordinal)) return 24;
        if (className.Contains("BattleItemData", StringComparison.Ordinal)) return 25;
        return 26; // AUCTION_CATEGORY_NORMAL
    }

    // Cooked subclasses omit properties equal to their class default. WeaponGroup is intrinsic to
    // each concrete HuxleyWeaponData_* class, so restore that default for authoritative checks.
    private static string DefaultWeaponGroup(string className)
    {
        (string Class, string Group)[] groups =
        [
            ("ShotGun", "EWG_ShotGun"), ("Flinger", "EWG_Flinger"),
            ("Discharger", "EWG_Discharger"), ("MachineGun", "EWG_MachineGun"),
            ("RocketLauncher", "EWG_RocketLauncher"), ("OpticalRifle", "EWG_OpticalRifle"),
            ("Sniper", "EWG_Sniper"), ("Regulator", "EWG_Regulator"),
            ("Inquisitor", "EWG_Inquisitor")
        ];
        foreach (var value in groups)
            if (className.Contains(value.Class, StringComparison.OrdinalIgnoreCase)) return value.Group;
        return "";
    }

    // HuxleyWeaponDataDescriptor inherits this restriction from the concrete weapon-data CDO.
    // Only these three base families set a non-None default; ProjRegulator explicitly clears the
    // RocketLauncher default and its subclasses inherit that cleared value.
    private static string DefaultExclusiveCombatStyle(string className)
    {
        if (className.Contains("ProjRegulator", StringComparison.OrdinalIgnoreCase)
            || className.Contains("ProjOpticalRifle", StringComparison.OrdinalIgnoreCase))
            return "CombatStyle_None";
        if (className.Contains("Discharger", StringComparison.OrdinalIgnoreCase)) return "CombatStyle_Enforcer";
        if (className.Contains("Sniper", StringComparison.OrdinalIgnoreCase)
            || className.Contains("InstantPiercing", StringComparison.OrdinalIgnoreCase))
            return "CombatStyle_Phantom";
        if (className.Contains("RocketLauncher", StringComparison.OrdinalIgnoreCase)) return "CombatStyle_Avenger";
        return "CombatStyle_None";
    }

    // ItemPos is a class default on the cooked armor subclasses and is therefore absent from
    // most exported instances. Recover the same EItemPos value from the concrete class name.
    // Armor classes derive from HuxleyArmorData, whose CDO (analysis/HuxleyGame-script-cdos.txt) sets
    // seven tune-up categories (ShieldMax, HealthProtection, RandomProtection, Energy, Level,
    // Contamination, Speed) at MaxTuneUps 4, TuneupLunaritesA=3 and TuneupFee=50. The table export
    // omits values equal to those defaults, so an armor entry without its own TuneUpCategories
    // (e.g. [Brave] Boots 230404, [Crusader] 231x09) still tunes in retail.
    private static bool IsArmorClass(string className) =>
        className.StartsWith("HuxleyMMOItemData_", StringComparison.Ordinal)
        || className == "HuxleyArmorData";

    private static void ApplyArmorTuneUpDefaults(Pending pending)
    {
        if (pending.TuneUpCategories.Count == 0) pending.TuneUpCategories = [4, 4, 4, 4, 4, 4, 4];
    }

    // Default__HuxleyArmorData and Default__HuxleyWeaponData both set TuneupLunaritesA=3 and
    // TuneupFee=50, and no subclass CDO overrides them. Each property inherits on its own: weapon
    // 100101 writes only TuneupFee=225 and still needs A lunarites, 140607 writes LunaritesB=3 but
    // HuxleyTuneupMenu asks for A first, and an explicit TuneupLunaritesA=0 line stays 0.
    private static bool IsWeaponClass(string className) =>
        className is "HuxleyWeaponData" or "HuxleyRapidWeaponData"
        || className.StartsWith("HuxleyWeaponData_", StringComparison.Ordinal);

    private static void ApplyTuneUpCostDefaults(Pending pending)
    {
        if (!pending.HasTuneupFee) pending.TuneupFee = 50;
        if (!pending.HasTuneupLunaritesA) pending.TuneupLunaritesA = 3;
    }

    private static string DefaultItemPos(string className)
    {
        (string Class, string Part)[] parts =
        [
            ("Helmet", "POS_Helmet"), ("Upbody", "POS_Upbody"),
            ("Lowbody", "POS_Lowbody"), ("Gloves", "POS_Gloves"),
            ("Boots", "POS_Boots")
        ];
        foreach (var value in parts)
            if (className.Contains(value.Class, StringComparison.OrdinalIgnoreCase)) return value.Part;
        return "";
    }

    // Retail slot subclasses carry their shape in class defaults:
    // Light=Minus (enum zero), Medium=At, Heavy=Plus, All=All.
    private static string SocketShape(string line)
    {
        if (line.Contains("_Medium", StringComparison.Ordinal)) return "SkillItemShape_At";
        if (line.Contains("_Heavy", StringComparison.Ordinal)) return "SkillItemShape_Plus";
        if (line.Contains("_All", StringComparison.Ordinal)) return "SkillItemShape_All";
        return "SkillItemShape_Minus";
    }

    private static bool TryInt(string line, string prefix, out int value)
    {
        value = 0;
        return line.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(line[prefix.Length..], out value);
    }

    // ENUM_RANK is contiguous in HuxleyPlayerWrapper.uc: SOLDIER1..13 (0..12),
    // SERGEANT1..15 (13..27), OFFICER1..24 (28..51).
    private static byte ParseRank(string value)
    {
        string name = value.StartsWith("RANK_", StringComparison.Ordinal) ? value[5..] : value;
        (string Prefix, int Count, int Base)[] groups =
            [("SOLDIER", 13, 0), ("SERGEANT", 15, 13), ("OFFICER", 24, 28)];
        foreach (var group in groups)
            if (name.StartsWith(group.Prefix, StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(group.Prefix.Length), out int ordinal)
                && ordinal >= 1 && ordinal <= group.Count)
                return (byte)(group.Base + ordinal - 1);
        return 0;
    }

    private static bool TryTuneupCap(string line, out int index, out int cap)
    {
        index = cap = 0;
        int close = line.IndexOf(']');
        const string capKey = "MaxTuneUps=";
        int capAt = line.IndexOf(capKey, StringComparison.Ordinal);
        if (close < 17 || capAt < 0 || !int.TryParse(line.AsSpan(17, close - 17), out index)) return false;
        int end = line.IndexOf(')', capAt);
        if (end < 0) end = line.Length;
        return int.TryParse(line.AsSpan(capAt + capKey.Length, end - capAt - capKey.Length), out cap)
            && index >= 0 && cap > 0;
    }

    private static bool TryExchangeCost(string line, out ExchangeCost cost)
    {
        cost = new(0, 0);
        const string itemKey = "ExchangeItemIndex=";
        const string countKey = "ExchangeItemCount=";
        int itemAt = line.IndexOf(itemKey, StringComparison.Ordinal);
        int countAt = line.IndexOf(countKey, StringComparison.Ordinal);
        if (itemAt < 0 || countAt < 0) return false;
        int comma = line.IndexOf(',', itemAt);
        int close = line.IndexOf(')', countAt);
        if (comma < 0 || close < 0
            || !int.TryParse(line.AsSpan(itemAt + itemKey.Length, comma - itemAt - itemKey.Length), out int itemId)
            || !int.TryParse(line.AsSpan(countAt + countKey.Length, close - countAt - countKey.Length), out int count)
            || itemId <= 0 || count <= 0) return false;
        cost = new(itemId, count);
        return true;
    }
}
