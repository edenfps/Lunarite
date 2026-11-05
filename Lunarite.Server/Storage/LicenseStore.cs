using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;

namespace Lunarite.Storage;

public sealed class LicenseStore
{
    private readonly ILogger _logger;
    private readonly CharacterStore _characterStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly PlayerStore _playerStore;

    public enum AcquireResult : byte
    {
        Ok = 0, Error = 1, AlreadyHave = 2, PreLicense = 3, Class = 4, Lp = 5, WrongStyle = 6,
        NotExist = 7, NotHave = 8, NotEnoughLevel = 9
    }

    private readonly object _lock = new();
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "playerlicenses.json");
    private readonly Dictionary<int, HashSet<int>> _owned;

    private readonly LicenseCatalog _licenseCatalog;

    private readonly AtomicJson _atomicJson;

    public LicenseStore(ILogger<LicenseStore> logger, CharacterStore characterStore,
        DurativeItemStore durativeItemStore, PlayerStore playerStore, LicenseCatalog licenseCatalog, AtomicJson atomicJson)
    {
        _logger = logger;
        _characterStore = characterStore;
        _durativeItemStore = durativeItemStore;
        _playerStore = playerStore;
        _licenseCatalog = licenseCatalog;
        _atomicJson = atomicJson;
        _owned = Load();
    }

    private Dictionary<int, HashSet<int>> Load()
    {
        try { return _atomicJson.Read<Dictionary<int, HashSet<int>>>(_path) ?? []; }
        catch (Exception ex) { _logger.LogWarning("[LicenseStore] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try { _atomicJson.Write(_path, JsonSerializer.Serialize(_owned, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[LicenseStore] save failed: {0}", ex.Message); }
    }

    public int[] Owned(int pid) { lock (_lock) return _owned.GetValueOrDefault(pid)?.Order().ToArray() ?? []; }

    public bool Has(int pid, int license)
    {
        lock (_lock) return _owned.TryGetValue(pid, out var set) && set.Contains(license);
    }

    public bool HasPermanentLicenses(int pid)
    {
        lock (_lock) return _owned.TryGetValue(pid, out var set) && set.Count != 0;
    }

    // Retail term-license items carry their target license index in the active ECIG_TermLicense
    // (group 5) OptionValue. HuxleyLicenseManager has no separate durative-item lookup: all of its
    // permission checks consume LicenseIndexList, so wire lists must merge these temporary grants.
    public int[] Effective(int pid, int? unixSeconds = null)
    {
        int[] permanent = Owned(pid);
        return permanent.Concat(_durativeItemStore.Active(pid, unixSeconds)
                .Where(x => x.CityItemGroup == 5 && _licenseCatalog.Get(x.OptionValue) != null)
                .Select(x => x.OptionValue))
            .Distinct().Order().ToArray();
    }

    // Exact HuxleyLicenseManager.HaveWeaponLicense rule. An omitted enum property is the cooked
    // class default LevelClass_S for the final link in each C/B/A/S weapon-license chain.
    public bool HaveWeaponLicense(int pid, ItemCatalog.Item weapon, int? unixSeconds = null)
    {
        if (weapon.WeaponGroup.Length == 0) return false;
        int requiredClass = LevelClass(weapon.EquipLevel);
        return Effective(pid, unixSeconds).Select(_licenseCatalog.Get).Any(x => x != null
            && x.WeaponGroups.Count != 0 && x.WeaponGroups[0] == weapon.WeaponGroup
            && LevelClass(x.PermissionLevelClass) >= requiredClass);
    }

    private static int LevelClass(int level) => level >= 30 ? 3 : level >= 20 ? 2 : level >= 10 ? 1 : 0;

    private static int LevelClass(string value) => value switch
    {
        "LevelClass_C" => 0, "LevelClass_B" => 1, "LevelClass_A" => 2, _ => 3
    };

    public int AuctionDiscountPercent(int pid)
        => CityTalentDiscountPercent(pid, "AuctionTalent");

    public int TuneupDiscountPercent(int pid)
        => CityTalentDiscountPercent(pid, "TuneupTalent");

    public int RepairDiscountPercent(int pid)
        => CityTalentDiscountPercent(pid, "RepairTalent");

    private int CityTalentDiscountPercent(int pid, string talentClass)
    {
        // HuxleyLicenseManager builds CityTalentDatum from the complete LicenseIndexList,
        // which includes active ECIG_TermLicense grants as well as permanent purchases.
        return Effective(pid).Select(_licenseCatalog.Get)
            .Where(x => x != null && x.ClassName.Contains(talentClass, StringComparison.Ordinal))
            .Select(x => x!.DiscountAmount).DefaultIfEmpty(0).Max();
    }

    public bool Grant(int pid, int license)
    {
        if (_licenseCatalog.Get(license) == null) return false;
        lock (_lock)
        {
            if (!_owned.TryGetValue(pid, out var set)) set = _owned[pid] = [];
            if (!set.Add(license)) return false;
            Save(); return true;
        }
    }

    public AcquireResult TryAcquire(int pid, int licenseId)
    {
        var data = _licenseCatalog.Get(licenseId);
        if (data == null) return AcquireResult.NotExist;
        var character = _characterStore.Get(pid);
        var progress = _playerStore.GetProgress(pid);
        // HuxleyLicenseDataDescriptor qualifies both ownership and prerequisite rows through
        // LicenseManager.HaveLicense, whose sole source is the merged LicenseIndexList. Active
        // ECIG_TermLicense grants are therefore real licenses for this transaction as well as for
        // equip/skill permission checks; they remain absent from permanent ownership/refunds.
        var effective = Effective(pid).ToHashSet();
        lock (_lock)
        {
            _owned.TryGetValue(pid, out var current);
            if (effective.Contains(licenseId)) return AcquireResult.AlreadyHave;
            if (data.PrecedingLicenseIndex != 0 && !effective.Contains(data.PrecedingLicenseIndex))
                return AcquireResult.PreLicense;
            if (progress.Level < data.EquipLevel) return AcquireResult.NotEnoughLevel;
            string expectedStyle = character?.Style switch
            {
                1 => "CombatStyle_Enforcer", 2 => "CombatStyle_Avenger", 3 => "CombatStyle_Phantom", _ => ""
            };
            if (data.CombatStyle.Length != 0 && data.CombatStyle != expectedStyle) return AcquireResult.WrongStyle;
            string expectedTribe = CharacterStore.GetTribe(character?.Index ?? 0) switch
            {
                1 => "Tribe_One", 2 => "Tribe_Syn", 3 => "Tribe_Alterlabor", 4 => "Tribe_Alternix", _ => ""
            };
            if (data.PermissionTribeType.Length != 0 && data.PermissionTribeType != expectedTribe) return AcquireResult.Class;
            if (!_playerStore.TrySpendLicensePoints(pid, data.LicensePoint)) return AcquireResult.Lp;
            if (!_owned.TryGetValue(pid, out current)) current = _owned[pid] = [];
            current.Add(licenseId); Save(); return AcquireResult.Ok;
        }
    }

    public void Reset(int pid)
    {
        lock (_lock) { if (_owned.Remove(pid)) Save(); }
    }

    public int ClearAndRefund(int pid)
    {
        lock (_lock)
        {
            if (!_owned.TryGetValue(pid, out var set) || set.Count == 0) return 0;
            int refund = set.Select(id => _licenseCatalog.Get(id)?.LicensePoint ?? 0).Sum();
            _owned.Remove(pid); Save(); return refund;
        }
    }
}
