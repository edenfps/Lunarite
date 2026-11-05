using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

// Persistent timed/cash entitlements carried by PlayerInfo, QuestCreate, and native city op231.
public sealed class DurativeItemStore
{
    private readonly ILogger _logger;

    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "durativeitems.json");
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    private readonly Dictionary<int, List<DurativeItem>> Data;

    private readonly AtomicJson _atomicJson;

    public DurativeItemStore(ILogger<DurativeItemStore> logger, AtomicJson atomicJson)
    {
        _logger = logger;
        _atomicJson = atomicJson;
        Data = Load();
    }

    private Dictionary<int, List<DurativeItem>> Load()
    {
        try { return _atomicJson.Read<Dictionary<int, List<DurativeItem>>>(PathName, Json) ?? []; }
        catch (Exception ex) { _logger.LogWarning("[DurativeItems] load failed: {0}", ex.Message); return []; }
    }

    private void Save()
    {
        try { _atomicJson.Write(PathName, JsonSerializer.Serialize(Data, Json)); }
        catch (Exception ex) { _logger.LogWarning("[DurativeItems] save failed: {0}", ex.Message); }
    }

    private static DurativeItem Clone(DurativeItem x) => new()
    {
        ItemID = x.ItemID,
        OptionValue = x.OptionValue,
        ExpirationDate = x.ExpirationDate,
        AddInfo = x.AddInfo,
        CityItemGroup = x.CityItemGroup
    };

    // ServerPolicy.OpenAllWeaponSlots (not retail): weapon slots 3 and 4 (cells 2/3) open for everyone.
    // HuxleyPlayerWrapper.IsOpenedWeaponSlot only looks for an ECIG_OpenWeaponSlot(9) entry with
    // the slot as OptionValue, and HasBeenExpired treats ExpirationDate 0 as permanent. ItemID 0
    // has no table row, so HuxleyDurationItemMenu skips these entries; they are never persisted.
    private const byte OpenWeaponSlotGroup = 9;

    private IEnumerable<DurativeItem> PolicyGrants() => ServerPolicy.OpenAllWeaponSlots
        ? [new DurativeItem { OptionValue = 2, CityItemGroup = OpenWeaponSlotGroup },
           new DurativeItem { OptionValue = 3, CityItemGroup = OpenWeaponSlotGroup }]
        : [];

    public List<DurativeItem> Active(int pid, int? unixSeconds = null)
    {
        lock (Sync)
        {
            int now = unixSeconds ?? checked((int)Math.Min(int.MaxValue, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            // The clock sweep owns removal so it can publish license revocation to a
            // client that already loaded the entitlement. Reads must not eat that event.
            var active = Data.TryGetValue(pid, out var items)
                ? items.Where(x => x.ExpirationDate == 0 || x.ExpirationDate > now).Select(Clone).ToList()
                : [];
            // Policy grants go last so the duration menu's index-keyed list has no gaps.
            active.AddRange(PolicyGrants().Where(grant => !active.Any(x =>
                x.CityItemGroup == grant.CityItemGroup && x.OptionValue == grant.OptionValue)));
            return active;
        }
    }

    // Native op230 expires the client's already-loaded array against a server timestamp.
    // Keep persistence on that same deterministic boundary for the online clock sweep.
    public List<DurativeItem> PruneExpired(int pid, int unixSeconds)
    {
        lock (Sync)
        {
            if (!Data.TryGetValue(pid, out var items)) return [];
            var expired = items
                .Where(x => x.ExpirationDate > 0 && x.ExpirationDate <= unixSeconds)
                .Select(Clone).ToList();
            if (expired.Count == 0) return expired;
            items.RemoveAll(x => x.ExpirationDate > 0 && x.ExpirationDate <= unixSeconds);
            if (items.Count == 0) Data.Remove(pid);
            Save();
            return expired;
        }
    }

    // Mirrors HuxleyPlayerWrapper.CanUseDurationItem. Duration is minutes; the retail
    // HuxleyDurationItemMenu reconstructs start time as expiration - CityItemDuration * 60.
    public bool TryActivate(int pid, ItemCatalog.Item item, out DurativeItem? activated,
        int? unixSeconds = null, bool replaceExistingGroup = false)
        => TryActivate(pid, item, out activated, out _, unixSeconds, replaceExistingGroup);

    public bool TryActivate(int pid, ItemCatalog.Item item, out DurativeItem? activated,
        out List<DurativeItem> replaced, int? unixSeconds = null, bool replaceExistingGroup = false)
    {
        activated = null;
        replaced = [];
        if (item.EntryIndex <= 0 || item.CityItemGroup == 0 || item.CityItemDuration < 0) return false;
        lock (Sync)
        {
            int now = unixSeconds ?? checked((int)Math.Min(int.MaxValue, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            if (!Data.TryGetValue(pid, out var items)) Data[pid] = items = [];
            // Ignore elapsed grants for overlap checks, but leave them for PruneExpired
            // to return to the online clock sweep (and its client notifications).
            bool IsLive(DurativeItem x) => x.ExpirationDate == 0 || x.ExpirationDate > now;

            if (replaceExistingGroup)
            {
                replaced = items.Where(x => IsLive(x) && x.CityItemGroup == item.CityItemGroup)
                    .Select(Clone).ToList();
                items.RemoveAll(x => IsLive(x) && x.CityItemGroup == item.CityItemGroup);
            }

            if (!replaceExistingGroup && item.CanBeOverlapped)
            {
                if (items.Any(x => IsLive(x) && x.ItemID == item.EntryIndex)
                    || items.Any(x => IsLive(x) && x.CityItemGroup == item.CityItemGroup && x.OptionValue == item.GeneralValue))
                    return false;
            }
            else if (!replaceExistingGroup && items.Any(x => IsLive(x) && x.CityItemGroup == item.CityItemGroup)) return false;

            long expiration = item.CityItemDuration == 0 ? 0L : (long)now + item.CityItemDuration * 60L;
            activated = new DurativeItem
            {
                ItemID = item.EntryIndex,
                OptionValue = item.GeneralValue,
                ExpirationDate = (int)Math.Min(int.MaxValue, expiration),
                AddInfo = 0,
                CityItemGroup = item.CityItemGroup
            };
            items.Add(Clone(activated));
            Save();
            return true;
        }
    }

    // The item is consumed after activation under MultiStoreTransaction. If that consume loses
    // a concurrent race, undo precisely this activation and restore its displaced group.
    public void RollBackActivation(int pid, DurativeItem activated,
        IReadOnlyCollection<DurativeItem> replaced)
    {
        lock (Sync)
        {
            if (!Data.TryGetValue(pid, out var items)) return;
            int index = items.FindIndex(x => x.ItemID == activated.ItemID
                && x.OptionValue == activated.OptionValue
                && x.ExpirationDate == activated.ExpirationDate
                && x.CityItemGroup == activated.CityItemGroup);
            if (index < 0) return;
            items.RemoveAt(index);
            items.AddRange(replaced.Select(Clone));
            if (items.Count == 0) Data.Remove(pid);
            Save();
        }
    }

    public void Revoke(int pid, int itemId)
    {
        lock (Sync)
        {
            if (!Data.TryGetValue(pid, out var items)) return;
            if (items.RemoveAll(x => x.ItemID == itemId) == 0) return;
            if (items.Count == 0) Data.Remove(pid);
            Save();
        }
    }

    public void ReplaceAll(int pid, IEnumerable<DurativeItem> items)
    {
        lock (Sync)
        {
            var copy = items.Where(x => x.ItemID > 0).Select(Clone).ToList();
            if (copy.Count == 0) Data.Remove(pid); else Data[pid] = copy;
            Save();
        }
    }

    public void Reset(int pid)
    {
        lock (Sync) { if (Data.Remove(pid)) Save(); }
    }
}
