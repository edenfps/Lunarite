using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

// Wardrobe (transmog, docs/WARDROBE.md): armor looks collected per account and gender, and the look each
// character shows per armor slot. Stats always come from the armor really equipped.
public sealed class WardrobeStore
{
    public const int PartCount = 5;      // helmet, upbody, lowbody, gloves, boots (ARMOR inventory cells)
    public const int OwnArmor = 0;       // slot shows the equipped piece
    public const int HiddenHelmet = -1;  // helmet slot only: nothing on the head

    public sealed class Data
    {
        // "userId:F" / "userId:M" -> collected armor item IDs, shared by every character of that gender on the account
        // (user, 2026-11-11). Older "userId:species" keys are merged into these when the store loads.
        public Dictionary<string, SortedSet<int>> Collections { get; set; } = [];
        // pid -> look per slot (OwnArmor, HiddenHelmet or an item ID).
        public Dictionary<int, int[]> Looks { get; set; } = [];
        // One-time backfill from current inventories and the backup history is done.
        public bool Backfilled { get; set; }
    }

    private readonly ILogger _logger;
    private readonly AtomicJson _atomicJson;
    private readonly object _lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "wardrobe.json");
    private readonly Data _data;

    public WardrobeStore(ILogger<WardrobeStore> logger, AtomicJson atomicJson)
    {
        _logger = logger;
        _atomicJson = atomicJson;
        try { _data = _atomicJson.Read<Data>(PathName) ?? new(); }
        catch (Exception ex) { _logger.LogWarning("[Wardrobe] load failed: {0}", ex.Message); _data = new(); }
        if (MigrateSpeciesKeys()) Save();
    }

    private void Save()
    {
        try { _atomicJson.Write(PathName, JsonSerializer.Serialize(_data)); }
        catch (Exception ex) { _logger.LogWarning("[Wardrobe] save failed: {0}", ex.Message); }
    }

    // Species TypeIndex 6 (SYN-F) and 16 (ATN-F) are female, 1 (ONE-M) and 11 (ATR-M) male.
    public static bool IsFemale(byte species) => species is 6 or 16;

    public static string Key(int userId, byte species) => userId + ":" + (IsFemale(species) ? "F" : "M");

    // Per-species collections (the first release) become per-gender ones.
    private bool MigrateSpeciesKeys()
    {
        bool changed = false;
        foreach (string key in _data.Collections.Keys.ToList())
        {
            int colon = key.IndexOf(':');
            if (colon < 0 || !int.TryParse(key[..colon], out int userId) || !byte.TryParse(key[(colon + 1)..], out byte species))
                continue;
            string target = Key(userId, species);
            if (!_data.Collections.TryGetValue(target, out var set)) set = _data.Collections[target] = [];
            set.UnionWith(_data.Collections[key]);
            _data.Collections.Remove(key);
            changed = true;
        }
        return changed;
    }

    public bool Backfilled
    {
        get { lock (_lock) return _data.Backfilled; }
    }

    public void MarkBackfilled()
    {
        lock (_lock) { _data.Backfilled = true; Save(); }
    }

    // Adds the IDs; returns how many were new.
    public int Collect(string key, IEnumerable<int> itemIds)
    {
        lock (_lock)
        {
            if (!_data.Collections.TryGetValue(key, out var set)) set = _data.Collections[key] = [];
            int added = itemIds.Count(set.Add);
            if (added != 0) Save();
            return added;
        }
    }

    public List<int> Collection(string key)
    {
        lock (_lock) return _data.Collections.TryGetValue(key, out var set) ? set.ToList() : [];
    }

    public bool Has(string key, int itemId)
    {
        lock (_lock) return _data.Collections.TryGetValue(key, out var set) && set.Contains(itemId);
    }

    public int[] Looks(int pid)
    {
        lock (_lock)
            return _data.Looks.TryGetValue(pid, out var looks) && looks.Length == PartCount
                ? (int[])looks.Clone() : new int[PartCount];
    }

    public void SetLook(int pid, int part, int look)
    {
        if (part < 0 || part >= PartCount) return;
        lock (_lock)
        {
            if (!_data.Looks.TryGetValue(pid, out var looks) || looks.Length != PartCount)
                looks = _data.Looks[pid] = new int[PartCount];
            looks[part] = look;
            if (looks.All(x => x == OwnArmor)) _data.Looks.Remove(pid);
            Save();
        }
    }

    public void DeleteCharacter(int pid)
    {
        lock (_lock) if (_data.Looks.Remove(pid)) Save();
    }
}
