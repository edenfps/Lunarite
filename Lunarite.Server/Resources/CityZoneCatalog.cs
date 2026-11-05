using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

// Exported HuxleyCityZoneData is the authoritative namespace accepted by MoveMap/LoadMap.
public sealed class CityZoneCatalog
{
    private readonly ILogger _logger;
    private readonly QuestCatalog _questCatalog;

    public enum Faction : byte { Sapiens = 1, Alternatives = 2 }

    public sealed record Zone(int EntryIndex, string ZoneName, string Level, string ZoneType,
        Faction OwnerFaction, IReadOnlyList<string> Areas);

    private readonly Dictionary<int, Zone> _zones = [];
    private readonly object _lock = new();
    private bool _loaded;

    public CityZoneCatalog(ILogger<CityZoneCatalog> logger, QuestCatalog questCatalog)
    {
        _logger = logger;
        _questCatalog = questCatalog;
    }

    public Zone? Get(int entryIndex)
    {
        EnsureLoaded();
        return _zones.GetValueOrDefault(entryIndex);
    }

    public bool IsKnown(int entryIndex) => Get(entryIndex) != null;

    public IReadOnlyList<Zone> All
    {
        get { EnsureLoaded(); lock (_lock) return [.. _zones.Values]; }
    }

    // HuxleyTableDepot keeps two independent city-zone tables and selects one from the
    // character's tribe. A valid index from the opposite table is not a legal destination.
    public bool IsAvailableToCharacter(int entryIndex, byte characterIndex)
    {
        var zone = Get(entryIndex);
        if (zone == null) return false;
        var faction = characterIndex >= 10 ? Faction.Alternatives : Faction.Sapiens;
        return zone.OwnerFaction == faction;
    }

    // MoveMap.Position and SendPosition are zero-based indices into HuxleyCityZoneData.Areas.
    // Zones with no explicit area array use the engine's default position zero.
    public bool IsValidArea(int entryIndex, int areaIndex, byte characterIndex,
        Func<string, bool>? clientHasMap = null)
    {
        var zone = Get(entryIndex);
        if (zone == null || !IsAvailableToCharacter(entryIndex, characterIndex) || areaIndex < 0)
            return false;
        // Building travel loads Level as a .war package, and the supplied clients lack two retail
        // building maps (travelling there strands the player on a loading screen).
        if (zone.ZoneType == "Zone_Building"
            && !(clientHasMap ?? _questCatalog.ClientHasMap)(zone.Level))
            return false;
        return zone.Areas.Count == 0 ? areaIndex == 0 : areaIndex < zone.Areas.Count;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            string? dir = AppContext.BaseDirectory;
            string? path = null;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "data", "tables", "HuxleyCityTable.txt");
                if (File.Exists(candidate)) { path = candidate; break; }
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            if (path == null)
            {
                _logger.LogWarning("[CityZoneCatalog] HuxleyCityTable.txt not found.");
                return;
            }

            // The owning table comes from the "// Reference:" line before each object.
            Faction pendingFaction = 0;
            Pending? zone = null;
            int depth = 0;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (zone == null && line.StartsWith("// Reference:", StringComparison.Ordinal))
                {
                    if (line.Contains(".SapiensCityZones.", StringComparison.Ordinal))
                        pendingFaction = Faction.Sapiens;
                    else if (line.Contains(".AlternativesCityZones.", StringComparison.Ordinal))
                        pendingFaction = Faction.Alternatives;
                    continue;
                }
                if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
                {
                    if (zone == null && line.Contains("class=HuxleyGame.HuxleyCityZoneData", StringComparison.OrdinalIgnoreCase))
                    {
                        zone = new Pending { OwnerFaction = pendingFaction };
                        depth = 1;
                    }
                    else if (zone != null) depth++;
                    continue;
                }
                if (zone == null) continue;
                if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
                {
                    if (--depth == 0)
                    {
                        if (zone.Level.Length != 0 && zone.OwnerFaction != 0)
                            _zones[zone.EntryIndex] = zone.Build();
                        zone = null;
                    }
                    continue;
                }
                if (depth != 1) continue;
                int split = line.IndexOfAny(['=', '[']);
                if (split > 0 && Fields.TryGetValue(line[..split], out var read))
                    read(zone, line[(line.IndexOf('=') + 1)..], line);
            }
            _logger.LogInformation("[CityZoneCatalog] loaded {0} retail city zones.", _zones.Count);
        }
    }

    // One HuxleyCityZoneData object while it is being read. EntryIndex is omitted when it keeps
    // its zero default (Nostalonia_A, the first Sapiens zone).
    private sealed class Pending
    {
        public int EntryIndex;
        public string ZoneName = "", Level = "", ZoneType = "";
        public Faction OwnerFaction;
        public List<string> Areas = [];

        public Zone Build() => new(EntryIndex, ZoneName, Level, ZoneType, OwnerFaction, Areas.ToArray());
    }

    private delegate void FieldReader(Pending zone, string value, string line);

    private static readonly Regex AreaRx = new(
        "^Areas\\[(?<index>\\d+)\\]=\\(AreaName=\\\"(?<name>[^\\\"]+)\\\"\\)$",
        RegexOptions.CultureInvariant);

    // Exported property name (the text before '=' or '[') -> how a zone reads it.
    private static readonly Dictionary<string, FieldReader> Fields = new(StringComparer.Ordinal)
    {
        ["ZoneName"] = (z, v, _) => z.ZoneName = Unquote(v),
        ["Level"] = (z, v, _) => z.Level = Unquote(v),
        ["ZoneType"] = (z, v, _) => z.ZoneType = v,
        ["EntryIndex"] = (z, v, _) => { if (int.TryParse(v, out int n)) z.EntryIndex = n; },
        // Areas[n]=(AreaName="..."); elements must arrive in index order.
        ["Areas"] = (z, _, line) =>
        {
            var match = AreaRx.Match(line);
            if (match.Success && int.TryParse(match.Groups["index"].Value, out int areaIndex)
                && areaIndex == z.Areas.Count)
                z.Areas.Add(match.Groups["name"].Value);
        },
    };

    private static string Unquote(string value) => value.Trim().Trim('"');
}
