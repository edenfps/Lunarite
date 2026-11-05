using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;

namespace Lunarite.Storage;

public sealed class CraftStore
{
    private readonly ILogger _logger;

    private readonly object _lock = new();
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "playerrecipes.json");
    private readonly Dictionary<int, HashSet<int>> _learned;

    private readonly CraftCatalog _craftCatalog;

    private readonly AtomicJson _atomicJson;

    public CraftStore(ILogger<CraftStore> logger, CraftCatalog craftCatalog, AtomicJson atomicJson)
    {
        _logger = logger;
        _craftCatalog = craftCatalog;
        _atomicJson = atomicJson;
        _learned = Load();
    }

    private Dictionary<int, HashSet<int>> Load()
    {
        try { return _atomicJson.Read<Dictionary<int, HashSet<int>>>(_path) ?? []; }
        catch (System.Exception ex) { _logger.LogWarning("[CraftStore] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try { _atomicJson.Write(_path, JsonSerializer.Serialize(_learned, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[CraftStore] save failed: {0}", ex.Message); }
    }

    public int[] Learned(int pid) { lock (_lock) return _learned.GetValueOrDefault(pid)?.Order().ToArray() ?? []; }

    public bool Learn(int pid, int recipe)
    {
        if (_craftCatalog.Get(recipe) == null) return false;
        lock (_lock)
        {
            if (!_learned.TryGetValue(pid, out var set)) set = _learned[pid] = [];
            if (!set.Add(recipe)) return false;
            Save(); return true;
        }
    }

    public bool Knows(int pid, int recipe)
    {
        lock (_lock) return _learned.TryGetValue(pid, out var set) && set.Contains(recipe);
    }

    public void Reset(int pid)
    {
        lock (_lock)
        {
            if (_learned.Remove(pid)) Save();
        }
    }
}
