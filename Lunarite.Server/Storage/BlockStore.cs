using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

public sealed class BlockStore
{
    private readonly ILogger _logger;

    private static readonly object Lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "blockedplayers.json");
    private readonly Dictionary<int, List<int>> Data;

    private readonly AtomicJson _atomicJson;

    public BlockStore(ILogger<BlockStore> logger, AtomicJson atomicJson)
    {
        _logger = logger;
        _atomicJson = atomicJson;
        Data = Load();
    }

    private Dictionary<int, List<int>> Load()
    {
        try { return _atomicJson.Read<Dictionary<int, List<int>>>(PathName) ?? []; }
        catch (Exception ex) { _logger.LogWarning("[Blocks] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try { _atomicJson.Write(PathName, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[Blocks] save failed: {0}", ex.Message); }
    }

    public IReadOnlyList<int> List(int pid)
    {
        lock (Lock) return Data.TryGetValue(pid, out var values) ? values.ToList() : [];
    }

    public byte Add(int pid, int targetPid)
    {
        if (pid <= 0 || targetPid <= 0 || pid == targetPid) return 2;
        lock (Lock)
        {
            if (!Data.TryGetValue(pid, out var values)) values = Data[pid] = [];
            if (values.Contains(targetPid)) return 5;
            if (values.Count >= 100) return 4;
            values.Add(targetPid); Save(); return 0;
        }
    }

    public byte Delete(int pid, int targetPid)
    {
        lock (Lock)
        {
            if (!Data.TryGetValue(pid, out var values) || !values.Remove(targetPid)) return 3;
            Save(); return 0;
        }
    }

    public void DeleteCharacter(int pid)
    {
        lock (Lock)
        {
            bool changed = Data.Remove(pid);
            foreach (var values in Data.Values)
                changed |= values.RemoveAll(x => x == pid) != 0;
            if (changed) Save();
        }
    }
}
