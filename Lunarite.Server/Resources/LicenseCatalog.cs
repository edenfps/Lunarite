using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

public sealed class LicenseCatalog
{
    private readonly ILogger _logger;

    public sealed record License(int EntryIndex, string ClassName, int LicensePoint,
        int PrecedingLicenseIndex, int EquipLevel, string CombatStyle, string PermissionTribeType,
        string LicenseType, int DiscountAmount, string PermissionLevelClass,
        IReadOnlyList<string> WeaponGroups);

    private readonly Dictionary<int, License> _licenses = [];
    private readonly object _lock = new();
    private bool _loaded;

    public LicenseCatalog(ILogger<LicenseCatalog> logger)
    {
        _logger = logger;
    }

    public License? Get(int id) { EnsureLoaded(); return _licenses.GetValueOrDefault(id); }

    public IReadOnlyCollection<License> All { get { EnsureLoaded(); return _licenses.Values; } }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            string? path = FindDump();
            if (path == null) { _logger.LogError("[LicenseCatalog] HuxleyLicenseTable.txt not found."); return; }
            Load(path);
            _logger.LogInformation("[LicenseCatalog] loaded {0} retail licenses.", _licenses.Count);
        }
    }

    private static string? FindDump()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string path = Path.Combine(dir, "data", "tables", "HuxleyLicenseTable.txt");
            if (File.Exists(path)) return path;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    private void Load(string path)
    {
        const string classPrefix = "class=HuxleyGame.";
        Pending? license = null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
            {
                int marker = line.IndexOf(classPrefix + "HuxleyLicenseData", StringComparison.Ordinal);
                license = marker < 0 ? null
                    : new Pending { ClassName = line[(marker + classPrefix.Length)..].Trim() };
                if (license != null) ApplyClassDefaults(license, license.ClassName);
                continue;
            }
            if (license == null) continue;
            if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
            {
                if (license.EntryIndex > 0) _licenses[license.EntryIndex] = license.Build();
                license = null;
                continue;
            }
            int split = line.IndexOfAny(['=', '[']);
            if (split > 0 && Fields.TryGetValue(line[..split], out var read))
                read(license, line[(line.IndexOf('=') + 1)..], line);
        }
    }

    // One HuxleyLicenseData* object while it is being read.
    private sealed class Pending
    {
        public string ClassName = "";
        public int EntryIndex, LicensePoint, PrecedingLicenseIndex, EquipLevel, DiscountAmount;
        public string CombatStyle = "", PermissionTribeType = "", LicenseType = "", PermissionLevelClass = "";
        public List<string> WeaponGroup = [];

        public License Build() => new(EntryIndex, ClassName, LicensePoint, PrecedingLicenseIndex,
            EquipLevel, CombatStyle, PermissionTribeType, LicenseType, DiscountAmount,
            PermissionLevelClass, WeaponGroup.ToArray());
    }

    // The export omits every property equal to its archetype, so each license starts from its
    // class's cooked default chain (analysis/HuxleyGame-script-cdos.txt). Without this, tier-1
    // city talents (703700/703710/703720) lose Default__HuxleyLicenseData_CityTalent's
    // DiscountAmount=5 that HuxleyLicenseManager.GetProperCityLicenseValue applies client-side.
    private static void ApplyClassDefaults(Pending license, string className)
    {
        const string weaponPerf = "HuxleyLicenseData_WeaponPerf";
        if (ClassParents.TryGetValue(className, out string? parent)) ApplyClassDefaults(license, parent);
        else if (className.StartsWith(weaponPerf, StringComparison.Ordinal) && className != weaponPerf)
            ApplyClassDefaults(license, weaponPerf); // all 18 WeaponPerf* subclasses
        if (ClassDefaults.TryGetValue(className, out var apply)) apply(license);
    }

    private static readonly Dictionary<string, Action<Pending>> ClassDefaults = new(StringComparer.Ordinal)
    {
        ["HuxleyLicenseData_Commanding"] = l => l.LicenseType = "LicenseType_Commanding",
        ["HuxleyLicenseData_ArmorPerf"] = l => l.LicenseType = "LicenseType_ArmorPerf",
        ["HuxleyLicenseData_CityTalent"] = l => { l.DiscountAmount = 5; l.LicenseType = "LicenseType_CityTalent"; },
        ["HuxleyLicenseData_BattleTalent"] = l => l.LicenseType = "LicenseType_BattleTalent",
        ["HuxleyLicenseData_Driving"] = l => l.LicenseType = "LicenseType_Driving",
        ["HuxleyLicenseData_Special"] = l => l.LicenseType = "LicenseType_Special",
        ["HuxleyLicenseData_Weapon"] = l => l.PermissionLevelClass = "LevelClass_S",
        ["HuxleyLicenseData_WeaponPerf"] = l => l.LicenseType = "LicenseType_WeaponPerf",
    };

    // Class -> cooked archetype class, for the classes the license table instantiates.
    private static readonly Dictionary<string, string> ClassParents = new(StringComparer.Ordinal)
    {
        ["HuxleyLicenseData_Commanding"] = "HuxleyLicenseData_LicenseSkill",
        ["HuxleyLicenseData_Special"] = "HuxleyLicenseData_LicenseSkill",
        ["HuxleyLicenseData_Squad"] = "HuxleyLicenseData_Commanding",
        ["HuxleyLicenseData_SquadLeader"] = "HuxleyLicenseData_Squad",
        ["HuxleyLicenseData_SquadMember"] = "HuxleyLicenseData_Squad",
        ["HuxleyLicenseData_AmmoSupply"] = "HuxleyLicenseData_SquadLeader",
        ["HuxleyLicenseData_Buff"] = "HuxleyLicenseData_SquadLeader",
        ["HuxleyLicenseData_Gathering"] = "HuxleyLicenseData_SquadLeader",
        ["HuxleyLicenseData_OverCoin"] = "HuxleyLicenseData_SquadLeader",
        ["HuxleyLicenseData_RangeExpansion"] = "HuxleyLicenseData_Buff",
        ["HuxleyLicenseData_Regeneration"] = "HuxleyLicenseData_Buff",
        ["HuxleyLicenseData_RelocatedSpawn"] = "HuxleyLicenseData_Buff",
        ["HuxleyLicenseData_ArmorPerfShield"] = "HuxleyLicenseData_ArmorPerf",
        ["HuxleyLicenseData_ArmorPerfSkill"] = "HuxleyLicenseData_ArmorPerf",
        ["HuxleyLicenseData_AuctionTalent"] = "HuxleyLicenseData_CityTalent",
        ["HuxleyLicenseData_RepairTalent"] = "HuxleyLicenseData_CityTalent",
        ["HuxleyLicenseData_TuneupTalent"] = "HuxleyLicenseData_CityTalent",
        ["HuxleyLicenseData_AVTalent"] = "HuxleyLicenseData_BattleTalent",
        ["HuxleyLicenseData_ConTalent"] = "HuxleyLicenseData_BattleTalent",
        ["HuxleyLicenseData_EnergyTalent"] = "HuxleyLicenseData_BattleTalent",
        ["HuxleyLicenseData_ShieldTalent"] = "HuxleyLicenseData_BattleTalent",
        ["HuxleyLicenseData_VelocityTalent"] = "HuxleyLicenseData_BattleTalent",
        ["HuxleyLicenseData_VitalityTalent"] = "HuxleyLicenseData_BattleTalent",
    };

    private delegate void FieldReader(Pending license, string value, string line);

    // Exported property name (the text before '=' or '[') -> how a license reads it.
    private static readonly Dictionary<string, FieldReader> Fields = new(StringComparer.Ordinal)
    {
        ["EntryIndex"] = Int((l, n) => l.EntryIndex = n),
        ["LicensePoint"] = Int((l, n) => l.LicensePoint = n),
        ["PrecedingLicenseIndex"] = Int((l, n) => l.PrecedingLicenseIndex = n),
        ["EquipLevel"] = Int((l, n) => l.EquipLevel = n),
        ["DiscountAmount"] = Int((l, n) => l.DiscountAmount = n),
        ["CombatStyle"] = (l, v, _) => l.CombatStyle = v,
        ["PermissionTribeType"] = (l, v, _) => l.PermissionTribeType = v,
        ["LicenseType"] = (l, v, _) => l.LicenseType = v,
        ["PermissionLevelClass"] = (l, v, _) => l.PermissionLevelClass = v,
        ["WeaponGroup"] = (l, v, line) => { if (line.Contains('=')) l.WeaponGroup.Add(v.Trim('"')); },
    };

    private static FieldReader Int(Action<Pending, int> set) =>
        (l, v, _) => { if (int.TryParse(v, out int n)) set(l, n); };
}
