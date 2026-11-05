using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Packets.Common;

namespace Lunarite.Storage;

public sealed class PersonalBuffStore
{
    private readonly ILogger _logger;

    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "personalbuffs.json");
    private readonly object _lock = new();
    private readonly Dictionary<int, List<PersonalBuff>> _buffs;

    private readonly AtomicJson _atomicJson;

    public PersonalBuffStore(ILogger<PersonalBuffStore> logger, AtomicJson atomicJson)
    {
        _logger = logger;
        _atomicJson = atomicJson;
        _buffs = Load();
    }

    private Dictionary<int, List<PersonalBuff>> Load()
    {
        try
        {
            return _atomicJson.Read<Dictionary<int, List<PersonalBuff>>>(_path,
                new JsonSerializerOptions { IncludeFields = true }) ?? [];
        }
        catch (Exception ex) { _logger.LogWarning("[PersonalBuffStore] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try
        {
            _atomicJson.Write(_path, JsonSerializer.Serialize(_buffs,
                new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        catch (Exception ex) { _logger.LogWarning("[PersonalBuffStore] save failed: {0}", ex.Message); }
    }

    public List<PersonalBuff> Active(int pid, int? unixSeconds = null)
    {
        lock (_lock)
        {
            if (!_buffs.TryGetValue(pid, out var list)) return [];
            int now = unixSeconds ?? checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            // The online clock sweep owns durable removal before op232 advances the client's
            // timer. A state read must not consume that expiration transition first.
            return list.Where(x => x.ExpirationDate == 0 || x.ExpirationDate > now)
                .Select(Clone).ToList();
        }
    }

    public List<PersonalBuff> PruneExpired(int pid, int unixSeconds)
    {
        lock (_lock)
        {
            if (!_buffs.TryGetValue(pid, out var list)) return [];
            var expired = list
                .Where(x => x.ExpirationDate > 0 && x.ExpirationDate <= unixSeconds)
                .Select(Clone).ToList();
            if (expired.Count == 0) return expired;
            list.RemoveAll(x => x.ExpirationDate > 0 && x.ExpirationDate <= unixSeconds);
            if (list.Count == 0) _buffs.Remove(pid);
            Save();
            return expired;
        }
    }

    public void Put(int pid, PersonalBuff buff)
    {
        lock (_lock)
        {
            if (!_buffs.TryGetValue(pid, out var list)) list = _buffs[pid] = [];
            PruneExpired(list);
            list.RemoveAll(x => x.ID == buff.ID || (buff.Group != 0 && x.Group == buff.Group));
            list.Add(Clone(buff));
            Save();
        }
    }

    // Returns the native op234 action/result: 5 added, 7 incremented,
    // 3 existing higher/equal level, 4 maximum cumulative level.
    public byte Apply(int pid, PersonalBuff incoming, byte maxLevel, out PersonalBuff result)
    {
        lock (_lock)
        {
            if (!_buffs.TryGetValue(pid, out var list)) list = _buffs[pid] = [];
            bool pruned = PruneExpired(list);
            var same = list.FirstOrDefault(x => x.ID == incoming.ID);
            if (same != null)
            {
                if (incoming.CanBeCumulated != 0)
                {
                    if (same.Level >= Math.Max(1, (int)maxLevel))
                    {
                        result = Clone(same);
                        if (pruned) Save();
                        return 4;
                    }
                    same.Level++;
                    same.ExpirationDate = incoming.ExpirationDate;
                    result = Clone(same);
                    Save();
                    return 7;
                }
                if (same.Level >= incoming.Level)
                {
                    result = Clone(same);
                    if (pruned) Save();
                    return 3;
                }
                list.Remove(same);
            }
            else if (incoming.Group != 0)
            {
                var grouped = list.FirstOrDefault(x => x.Group == incoming.Group);
                if (grouped != null)
                {
                    if (grouped.Level >= incoming.Level)
                    {
                        result = Clone(grouped);
                        if (pruned) Save();
                        return 3;
                    }
                    list.Remove(grouped);
                }
            }
            result = Clone(incoming);
            list.Add(Clone(incoming));
            Save();
            return 5;
        }
    }

    public bool TryDelete(int pid, int buffId, out PersonalBuff buff)
    {
        lock (_lock)
        {
            buff = null!;
            if (!_buffs.TryGetValue(pid, out var list)) return false;
            bool pruned = PruneExpired(list);
            int index = list.FindIndex(x => x.ID == buffId);
            if (index < 0)
            {
                if (pruned) Save();
                return false;
            }
            buff = Clone(list[index]);
            list.RemoveAt(index);
            Save();
            return true;
        }
    }

    public void Reset(int pid)
    {
        lock (_lock)
        {
            if (_buffs.Remove(pid)) Save();
        }
    }

    private static PersonalBuff Clone(PersonalBuff x) => new()
    {
        ID = x.ID, Group = x.Group, ModifierType = x.ModifierType,
        CanBeCumulated = x.CanBeCumulated, Level = x.Level, ExpirationDate = x.ExpirationDate
    };

    private static bool PruneExpired(List<PersonalBuff> list, int? unixSeconds = null)
    {
        int now = unixSeconds ?? checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return list.RemoveAll(x => x.ExpirationDate > 0 && x.ExpirationDate <= now) != 0;
    }
}
