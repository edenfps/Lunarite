using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

// The Simulation Center room menu lists only the client's enabled cooked entries
// (HuxleySimulationCenterRule.GetActiveGameLevels -> TableDepot.FindActiveBattleZones), so disabled
// or untabled battle maps cannot be chosen there. op155 carries the hosted map as a name, so the
// server can host a different level for rooms created on a listed entry. Persisted (simmaps.json).
public sealed class SimulationMapOverrides
{
    private readonly BattleZoneCatalog _battleZoneCatalog;

    // Levels present in the supplied client with their dependencies (analysis/audit/map-access.md
    // 2a/2b): disabled cooked entries plus untabled battle maps. BZC-014-Slums (no geometry) and
    // levels absent from the client are deliberately excluded.
    public static readonly IReadOnlyList<string> AvailableLevels =
    [
        "BZC-002-Pipes", "BZC-004-Transponder", "BZC-006-Tears", "BZC-010-Junkyard",
        "BZC-011-Crater", "BZC-012-Rig", "BZC-013-BStar", "BZP-004-Crossfire", "BZP-006-quakeB",
        "BZT-Test-CuttleFish", "BZP-005-Harbor", "BZC-007-Docks", "SQ-Game"
    ];

    // Table rows with no usable level of their own carry the maps that have no row; the patched client
    // labels them (tools/patches/skirmish_maps.py).
    private static readonly IReadOnlyDictionary<int, string> BuiltIn = new Dictionary<int, string>
    {
        [109] = "BZC-008-LargeQuake",
        [174] = "BZC-001-Earthquake",
        [180] = "BZC-007-Docks",
        [183] = "BZP-005-Harbor",
        [191] = "BZT-Test-CuttleFish"
    };

    private static readonly object Lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "simmaps.json");
    private Dictionary<int, string>? _overrides;

    private readonly AtomicJson _atomicJson;

    public SimulationMapOverrides(BattleZoneCatalog battleZoneCatalog, AtomicJson atomicJson)
    {
        _battleZoneCatalog = battleZoneCatalog;
        _atomicJson = atomicJson;
    }

    private Dictionary<int, string> Current() =>
        _overrides ??= _atomicJson.Read<Dictionary<int, string>>(PathName) ?? [];

    public string? LevelFor(int entryIndex)
    {
        lock (Lock) return Current().GetValueOrDefault(entryIndex) ?? BuiltIn.GetValueOrDefault(entryIndex);
    }

    public IReadOnlyDictionary<int, string> All
    {
        get { lock (Lock) return new Dictionary<int, string>(Current()); }
    }

    // Returns the canonical level name, or null when the level is not an available map.
    public string? Set(int entryIndex, string level)
    {
        string? canonical = AvailableLevels.FirstOrDefault(x => x.Equals(level, StringComparison.OrdinalIgnoreCase));
        if (canonical == null || _battleZoneCatalog.Get(entryIndex) == null) return null;
        lock (Lock)
        {
            Current()[entryIndex] = canonical;
            Save();
        }
        return canonical;
    }

    public bool Clear(int entryIndex)
    {
        lock (Lock)
        {
            if (!Current().Remove(entryIndex)) return false;
            Save();
            return true;
        }
    }

    private void Save() => _atomicJson.Write(PathName, JsonSerializer.Serialize(_overrides));

    internal void ResetForTest()
    {
        lock (Lock) { _overrides = null; _atomicJson.Delete(PathName); }
    }
}
