using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

// Persistent battle-zone war front (HuxleyBattleZoneData AxisID/AxisStart/BaseBattleZoneSpecies/
// RecessMinutes/LinkedBattleServers). Each axis is a chain of linked battlefields, walked from the
// Sapiens end (left link 0) to the Alternative end. Axis 0 is a real axis (the radar chain).
// Every map has an occupying species (ESpeciesType 1 Sapiens, 2 Alternative), seeded from its base
// species; base maps never change hands. One map per axis is the battleground. A battle-zone result
// there gives the map to the winner and pushes the battleground one step toward the loser's side
// (never onto a base), then the battleground rests for RecessMinutes.
public sealed class BattleFront
{
    private readonly ILogger _logger;
    private readonly BattleZoneCatalog _battleZoneCatalog;

    public enum FieldStatus : byte { Joinable = 0, Battle = 1, Recess = 2, Safe = 3 } // EBattleFieldStatus

    public sealed class State
    {
        public Dictionary<int, byte> Occupant { get; set; } = [];      // map entry -> species

        public Dictionary<int, int> Battleground { get; set; } = [];   // axis -> map entry

        public Dictionary<int, long> RecessUntil { get; set; } = [];   // map entry -> unix ms
    }

    public readonly record struct Outcome(int Map, byte FromSpecies, byte ToSpecies, int NextBattleground);

    private static readonly object Lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "battlefront.json");
    private State? _state;
    internal Func<long> Now = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private readonly AtomicJson _atomicJson;

    public BattleFront(ILogger<BattleFront> logger, BattleZoneCatalog battleZoneCatalog,
        AtomicJson atomicJson)
    {
        _logger = logger;
        _battleZoneCatalog = battleZoneCatalog;
        _atomicJson = atomicJson;
    }

    public IReadOnlyList<BattleZoneCatalog.Zone> Chain(int axis)
    {
        var members = _battleZoneCatalog.All.Where(z => z.OnFront && z.Axis == axis).ToList();
        var byServer = members.Where(z => z.BattleServerId > 0).ToDictionary(z => z.BattleServerId);
        var chain = new List<BattleZoneCatalog.Zone>();
        var current = members.Where(z => z.LinkedServers.Count > 0 && z.LinkedServers[0] == 0)
            .OrderBy(z => z.EntryIndex).FirstOrDefault();
        while (current != null && !chain.Contains(current))
        {
            chain.Add(current);
            int right = current.LinkedServers.Count > 1 ? current.LinkedServers[1] : 0;
            current = byServer.GetValueOrDefault(right);
        }
        // A malformed or partial link set still lists every field once, in index order.
        chain.AddRange(members.Where(z => !chain.Contains(z)).OrderBy(z => z.EntryIndex));
        return chain;
    }

    public IReadOnlyList<int> Axes => _battleZoneCatalog.All.Where(z => z.OnFront)
        .Select(z => (int)z.Axis).Distinct().Order().ToList();

    private static byte SeedSpecies(byte baseSpecies) =>
        baseSpecies is 1 or 3 ? (byte)1 : baseSpecies is 2 or 4 ? (byte)2 : (byte)0;

    private static bool IsBase(BattleZoneCatalog.Zone zone) => zone.BaseSpecies is 1 or 2;

    private State Current()
    {
        if (_state != null) return _state;
        try { _state = _atomicJson.Read<State>(PathName); }
        catch (Exception ex) { _logger.LogWarning("[BattleFront] load failed: {0}", ex.Message); }
        _state ??= new State();
        bool changed = false;
        foreach (int axis in Axes)
        {
            var chain = Chain(axis);
            foreach (var zone in chain)
                if (!_state.Occupant.ContainsKey(zone.EntryIndex))
                {
                    _state.Occupant[zone.EntryIndex] = SeedSpecies(zone.BaseSpecies);
                    changed = true;
                }
            if (!_state.Battleground.TryGetValue(axis, out int ground) || chain.All(z => z.EntryIndex != ground))
            {
                var start = chain.FirstOrDefault(z => z.AxisStart && !IsBase(z)) ?? chain.FirstOrDefault(z => !IsBase(z));
                if (start != null) { _state.Battleground[axis] = start.EntryIndex; changed = true; }
            }
        }
        if (changed) Save();
        return _state;
    }

    private void Save()
    {
        try { _atomicJson.Write(PathName, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[BattleFront] save failed: {0}", ex.Message); }
    }

    public byte OccupantOf(int map) { lock (Lock) return Current().Occupant.GetValueOrDefault(map); }

    // The server list's camp (nCamp, "dominant faction"): the species holding more contested
    // front fields, 0 on a tie. The client appends SpeciesName[camp] ("The Sapiens"/"The Alternative").
    public byte DominantSpecies()
    {
        lock (Lock)
        {
            var state = Current();
            int sapiens = 0, alternatives = 0;
            foreach (int axis in Axes)
                foreach (var zone in Chain(axis).Where(z => !IsBase(z)))
                {
                    byte occupant = state.Occupant.GetValueOrDefault(zone.EntryIndex);
                    if (occupant == 1) sapiens++;
                    else if (occupant == 2) alternatives++;
                }
            return sapiens > alternatives ? (byte)1 : alternatives > sapiens ? (byte)2 : (byte)0;
        }
    }

    public int BattlegroundOf(int axis) { lock (Lock) return Current().Battleground.GetValueOrDefault(axis); }

    public int RecessMinutesLeft(int map)
    {
        lock (Lock)
        {
            long left = Current().RecessUntil.GetValueOrDefault(map) - Now();
            return left <= 0 ? 0 : (int)Math.Ceiling(left / 60_000.0);
        }
    }

    public FieldStatus StatusOf(int map, bool battleRunning)
    {
        var zone = _battleZoneCatalog.Get(map);
        if (zone == null || !zone.OnFront || BattlegroundOf(zone.Axis) != map) return FieldStatus.Safe;
        if (battleRunning) return FieldStatus.Battle;
        return RecessMinutesLeft(map) > 0 ? FieldStatus.Recess : FieldStatus.Joinable;
    }

    // Only the current battleground of an axis may be entered, and not while it rests.
    public bool CanEnter(int map)
    {
        var zone = _battleZoneCatalog.Get(map);
        if (zone == null || !zone.OnFront) return true;
        return BattlegroundOf(zone.Axis) == map && RecessMinutesLeft(map) == 0;
    }

    // Applies a battleground result. Returns null for maps that are not a current, open battleground
    // (repeated per-player result reports of one match land inside the recess and are ignored).
    public Outcome? RecordResult(int map, byte winnerSpecies)
    {
        if (winnerSpecies is not (1 or 2)) return null;
        var zone = _battleZoneCatalog.Get(map);
        if (zone == null || !zone.OnFront) return null;
        lock (Lock)
        {
            var state = Current();
            if (state.Battleground.GetValueOrDefault(zone.Axis) != map) return null;
            if (state.RecessUntil.GetValueOrDefault(map) > Now()) return null;
            byte from = state.Occupant.GetValueOrDefault(map);
            if (!IsBase(zone)) state.Occupant[map] = winnerSpecies;
            var chain = Chain(zone.Axis);
            int index = chain.ToList().FindIndex(z => z.EntryIndex == map);
            // Sapiens hold the left end, Alternatives the right end: winners push outward toward
            // the losing side.
            int step = winnerSpecies == 1 ? 1 : -1;
            int nextIndex = index + step;
            int next = map;
            if (nextIndex >= 0 && nextIndex < chain.Count && !IsBase(chain[nextIndex]))
                next = chain[nextIndex].EntryIndex;
            state.Battleground[zone.Axis] = next;
            var nextZone = _battleZoneCatalog.Get(next);
            state.RecessUntil[next] = Now() + Math.Max(0, nextZone?.RecessMinutes ?? zone.RecessMinutes) * 60_000L;
            Save();
            return new Outcome(map, from, winnerSpecies, next);
        }
    }

    internal void ResetForTest()
    {
        lock (Lock)
        {
            _state = null;
            _atomicJson.Delete(PathName);
        }
    }
}
