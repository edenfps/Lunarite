using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

public sealed class FriendStore
{
    private readonly ILogger _logger;

    public sealed record Entry(int Pid, byte Relation);

    private static readonly object Lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "friends.json");
    private readonly Dictionary<int, List<Entry>> Data;

    private readonly AtomicJson _atomicJson;

    public FriendStore(ILogger<FriendStore> logger, AtomicJson atomicJson)
    {
        _logger = logger;
        _atomicJson = atomicJson;
        Data = Load();
    }

    private Dictionary<int, List<Entry>> Load()
    {
        try
        {
            return _atomicJson.Read<Dictionary<int, List<Entry>>>(PathName) ?? [];
        }
        catch (Exception ex) { _logger.LogWarning("[Friends] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try { _atomicJson.Write(PathName, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[Friends] save failed: {0}", ex.Message); }
    }

    public IReadOnlyList<Entry> List(int pid)
    {
        lock (Lock) return Data.TryGetValue(pid, out var entries) ? entries.ToList() : [];
    }

    // FRIEND_PROCESS_MSG values from exported HuxleySubsystem.uc.
    public byte Add(int pid, int targetPid, byte relation)
    {
        if (pid <= 0 || targetPid <= 0 || pid == targetPid) return 7; // INVALIDNAME
        lock (Lock)
        {
            if (!Data.TryGetValue(pid, out var entries)) entries = Data[pid] = [];
            if (entries.Any(x => x.Pid == targetPid)) return 2; // ADD_EXISTED
            if (entries.Count >= 100) return 3; // ADD_OVERMAX
            entries.Add(new Entry(targetPid, relation)); Save(); return 0;
        }
    }

    public byte Delete(int pid, int targetPid)
    {
        lock (Lock)
        {
            if (!Data.TryGetValue(pid, out var entries)) return 7;
            int removed = entries.RemoveAll(x => x.Pid == targetPid);
            if (removed == 0) return 7;
            Save(); return 4; // FRIEND_DELETE_SUCCESS
        }
    }

    public void DeleteCharacter(int pid)
    {
        lock (Lock)
        {
            bool changed = Data.Remove(pid);
            foreach (var entries in Data.Values)
                changed |= entries.RemoveAll(x => x.Pid == pid) != 0;
            if (changed) Save();
        }
    }
}
