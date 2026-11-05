using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging;

namespace Lunarite.Resources;

// Retail public-transport routes and fares from HuxleyCityTable.Transports.
public sealed class CityTransportCatalog
{
    private readonly ILogger _logger;

    public sealed record Route(int EntryIndex, string RouteName, int UseFee);

    private readonly Dictionary<int, Route> _routes = [];
    private readonly object _lock = new();
    private bool _loaded;

    public CityTransportCatalog(ILogger<CityTransportCatalog> logger)
    {
        _logger = logger;
    }

    public Route? Get(int entryIndex)
    {
        EnsureLoaded();
        return _routes.GetValueOrDefault(entryIndex);
    }

    public IReadOnlyCollection<Route> All
    {
        get { EnsureLoaded(); return _routes.Values; }
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
                _logger.LogWarning("[CityTransportCatalog] HuxleyCityTable.txt not found.");
                return;
            }

            Pending? route = null;
            int depth = 0;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("begin object", StringComparison.OrdinalIgnoreCase))
                {
                    if (route == null && line.Contains(
                            "class=HuxleyGame.HuxleyCityTransportData", StringComparison.OrdinalIgnoreCase))
                    {
                        route = new Pending();
                        depth = 1;
                    }
                    else if (route != null) depth++;
                    continue;
                }
                if (route == null) continue;
                if (line.Equals("end object", StringComparison.OrdinalIgnoreCase))
                {
                    if (--depth == 0)
                    {
                        // EntryIndex keeps its zero default (and is omitted) for route 0.
                        if (route.RouteName.Length != 0 && route.UseFee >= 0)
                            _routes[route.EntryIndex] = new Route(route.EntryIndex, route.RouteName, route.UseFee);
                        route = null;
                    }
                    continue;
                }
                if (depth != 1) continue;
                int split = line.IndexOf('=');
                if (split <= 0) continue;
                string value = line[(split + 1)..];
                switch (line[..split])
                {
                    case "RouteName":
                        route.RouteName = value.Trim().Trim('"');
                        break;
                    case "UseFee" when int.TryParse(value, out int useFee):
                        route.UseFee = useFee;
                        break;
                    case "EntryIndex" when int.TryParse(value, out int entryIndex):
                        route.EntryIndex = entryIndex;
                        break;
                }
            }
            _logger.LogInformation("[CityTransportCatalog] loaded {0} retail routes.", _routes.Count);
        }
    }

    // One HuxleyCityTransportData object while it is being read.
    private sealed class Pending
    {
        public int EntryIndex, UseFee;
        public string RouteName = "";
    }
}
