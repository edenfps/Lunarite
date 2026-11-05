using System;
using System.Collections.Generic;
using System.IO;

namespace Lunarite;

// All operator settings in one file: huxley.conf, KEY=VALUE per line, # starts a comment
// (huxley.conf.example lists every setting with its default). The file is HUXLEY_CONFIG, or the
// first huxley.conf found searching up from the server's folder, as data/ is. A setting already
// in the environment wins, so launch scripts and systemd EnvironmentFile keep working; the same
// format is valid as a systemd EnvironmentFile.
public static class SettingsFile
{
    public const string FileName = "huxley.conf";

    public static string? Find()
    {
        if (Environment.GetEnvironmentVariable("HUXLEY_CONFIG") is { Length: > 0 } configured)
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, FileName);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    // Applies the file's settings that the environment does not already set; returns how many.
    public static int Apply(string path)
    {
        int applied = 0;
        foreach (var (key, value) in Parse(File.ReadAllLines(path)))
        {
            if (Environment.GetEnvironmentVariable(key) != null) continue;
            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }
        return applied;
    }

    public static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int equals = line.IndexOf('=');
            if (equals <= 0) continue;
            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                value = value[1..^1];
            yield return (key, value);
        }
    }
}
