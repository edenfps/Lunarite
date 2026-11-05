using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Packets.Common;
using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Storage;

// Per-character persistence: aggregate item totals, slotted inventory, and progression, each
// stored as JSON next to the executable and keyed by character PID. All mutation runs under _lock.
public sealed partial class PlayerStore
{
    private readonly ILogger _logger;
    private readonly EconomyRates _economyRates;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();

    private readonly string _itemTotalsPath = Path.Combine(AppContext.BaseDirectory, "playerdata.json");
    private readonly Dictionary<int, Dictionary<int, int>> _itemTotals;

    private readonly string _progressPath = Path.Combine(AppContext.BaseDirectory, "playerprogress.json");
    private readonly Dictionary<int, PlayerProgress> _progress;

    private readonly string _inventoryPath = Path.Combine(AppContext.BaseDirectory, "playerinventory.json");
    private readonly Dictionary<int, List<GenericItem>> _inventory;

    // HuxleyMultiPlayerReplicationInfo.ComputePlayerLevelupGoalBP + DefaultGame.ini. The retail
    // client treats these as cumulative BP thresholds and uses a strict `goal < BP` comparison.
    private static readonly float[] LevelupBpSubParam =
    [
        1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1.2f, 1.28f,
        1.3f, 1.29f, 1.28f, 1.27f, 1.26f, 1.25f, 1.24f, 1.23f,
        1.29f, 1.33f, 1.35f, 1.36f, 1.36f, 1.35f, 1.34f, 1.33f,
        1.32f, 1.31f, 1.34f
    ];

    // Repair QT per missing durability point, by level class and item grade (HuxleyMMOGame.ini).
    private static readonly int[][] RepairQt =
    [
        [3, 4, 5, 6, 7, 10], [8, 9, 10, 11, 13, 17],
        [13, 14, 15, 16, 18, 25], [28, 30, 32, 34, 38, 50]
    ];

    // HuxleyItemSystem.EINVENTORY

    // HuxleyItemSystem item-state bits.
    private const byte ItemStateBind = 0x01;
    private const byte ItemStateBindWhenPickup = 0x02;
    private const byte ItemStateBindTag = 0x0F;
    private const byte ItemStateBindCountTag = 0xF0;
    private const int MaxResetBindCount = 5;
    private const int MaxTuneUpLevel = 6;
    private const int MaxDurability = 100;

    private readonly ItemCatalog _itemCatalog;
    private readonly RankTable _rankTable;
    private readonly ServerEvents _serverEvents;

    private readonly AtomicJson _atomicJson;

    public PlayerStore(ILogger<PlayerStore> logger, EconomyRates economyRates, ItemCatalog itemCatalog,
        RankTable rankTable, ServerEvents serverEvents, AtomicJson atomicJson)
    {
        _logger = logger;
        _economyRates = economyRates;
        _itemCatalog = itemCatalog;
        _rankTable = rankTable;
        _serverEvents = serverEvents;
        _atomicJson = atomicJson;
        _itemTotals = LoadItemTotals();
        _progress = LoadProgress();
        _inventory = LoadInventory();
        NormalizeSkillCells();
    }

    private Dictionary<int, Dictionary<int, int>> LoadItemTotals()
    {
        try
        {
            var totals = _atomicJson.Read<Dictionary<int, Dictionary<int, int>>>(_itemTotalsPath);
            if (totals != null) return totals;
        }
        catch (Exception ex) { _logger.LogWarning("[Store] load failed: {0}", ex.Message); }
        return new();
    }

    private Dictionary<int, PlayerProgress> LoadProgress()
    {
        try { return _atomicJson.Read<Dictionary<int, PlayerProgress>>(_progressPath) ?? new(); }
        catch (Exception ex) { _logger.LogWarning("[Store] progress load failed: {0}", ex.Message); }
        return new();
    }

    private Dictionary<int, List<GenericItem>> LoadInventory()
    {
        try { return _atomicJson.Read<Dictionary<int, List<GenericItem>>>(_inventoryPath) ?? new(); }
        catch (Exception ex) { _logger.LogWarning("[Store] inventory load failed: {0}", ex.Message); }
        return new();
    }

    private void SaveItemTotals()
    {
        try { _atomicJson.Write(_itemTotalsPath, JsonSerializer.Serialize(_itemTotals, JsonOptions)); }
        catch (Exception ex) { _logger.LogWarning("[Store] save failed: {0}", ex.Message); }
    }

    private void SaveProgress()
    {
        try { _atomicJson.Write(_progressPath, JsonSerializer.Serialize(_progress, JsonOptions)); }
        catch (Exception ex) { _logger.LogWarning("[Store] progress save failed: {0}", ex.Message); }
    }

    private void SaveInventory()
    {
        try { _atomicJson.Write(_inventoryPath, JsonSerializer.Serialize(_inventory, JsonOptions)); }
        catch (Exception ex) { _logger.LogWarning("[Store] inventory save failed: {0}", ex.Message); }
    }

    // Fresh state for a newly created character, so a reused PID never inherits stale data.
    public void ResetCharacter(int pid)
    {
        lock (_lock)
        {
            _progress[pid] = new PlayerProgress();
            _itemTotals.Remove(pid);
            _inventory.Remove(pid);
            SaveItemTotals();
            SaveProgress();
            SaveInventory();
            _logger.LogInformation("[Store] reset fresh character state for PID {0}.", pid);
        }
    }

    public void DeleteCharacter(int pid)
    {
        lock (_lock)
        {
            _itemTotals.Remove(pid);
            _progress.Remove(pid);
            _inventory.Remove(pid);
            SaveItemTotals();
            SaveProgress();
            SaveInventory();
            _logger.LogInformation("[Store] deleted all persisted state for PID {0}.", pid);
        }
    }

    // Callers must hold _lock.
    private PlayerProgress ProgressFor(int pid) =>
        _progress.TryGetValue(pid, out var progress) ? progress : _progress[pid] = new PlayerProgress();

    private void AddTotal(int pid, int itemId, int count)
    {
        if (!_itemTotals.TryGetValue(pid, out var totals)) totals = _itemTotals[pid] = [];
        totals[itemId] = totals.GetValueOrDefault(itemId) + count;
    }

    // Lowers a tracked total, dropping the entry at zero. Untracked items are left alone.
    private void ReduceTotal(int pid, int itemId, int count)
    {
        if (!_itemTotals.TryGetValue(pid, out var totals) || !totals.TryGetValue(itemId, out int total)) return;
        int next = total - count;
        if (next <= 0) totals.Remove(itemId); else totals[itemId] = next;
    }

    // HuxleyItemSystemHelper.FindEmptyCell for InventoryType.Main: the lowest unused cell, or -1.
    private static int FindEmptyCell(List<GenericItem> list, int capacity)
    {
        var used = list.Where(i => i.InventoryIndex == InventoryType.Main).Select(i => i.InventoryCell).ToHashSet();
        return Enumerable.Range(0, capacity).FirstOrDefault(c => !used.Contains((byte)c), -1);
    }

    private static GenericItem Clone(GenericItem item) => new()
    {
        InventoryIndex = item.InventoryIndex, InventoryCell = item.InventoryCell, ItemId = item.ItemId, Count = item.Count,
        State = item.State, LimitTime = item.LimitTime, TuneUpOptions = item.TuneUpOptions,
        Durability = item.Durability, Skill = item.Skill, Skill1 = item.Skill1
    };
}
