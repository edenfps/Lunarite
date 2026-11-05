using System;
using System.Collections.Generic;
using System.IO;

namespace Lunarite.Resources;

// Resolves the client-visible item keys used by cooked HuxleyItemTable rows. The Webzen INT
// localization is incomplete, so version-controlled recovery translations fill only its gaps.
public static class ItemLocalization
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Values = new(Load);

    public static string? Get(int itemId, string field) =>
        Values.Value.GetValueOrDefault($"{itemId}_{field}");

    private static IReadOnlyDictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Preserve supplied Webzen English text where it exists.
        LoadKeyValues(ClientFolder.Resolve(@"HuxleyGame\Localization\INT\Items.int"), values, false);

        // Development installs also carry an older flat English recovery source.
        LoadKeyValues(ClientFolder.Resolve("items.txt"), values, false);

        // Version-controlled translations are authoritative for keys absent from retail INT.
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "data", "loc", "Items.tsv");
            if (File.Exists(candidate))
            {
                LoadKeyValues(candidate, values, true);
                break;
            }
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return values;
    }

    private static void LoadKeyValues(string path, Dictionary<string, string> values, bool overwrite)
    {
        if (!File.Exists(path)) return;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '[' or '#' or '/' || !line.Contains('=')) continue;
            int separator = line.IndexOf('=');
            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim().Replace("\\n", " ");
            if (overwrite || !values.ContainsKey(key)) values[key] = value;
        }
    }
}
