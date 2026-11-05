using System;
using System.IO;
using System.Linq;

namespace Lunarite.Resources;

// The Huxley client install the server reads its map list and item text from.
public static class ClientFolder
{
    public static string Root =>
        Environment.GetEnvironmentVariable("HUXLEY_CLIENT_ROOT") is { Length: > 0 } root
            ? root : OperatingSystem.IsWindows() ? @"C:\Games\Huxley" : "/opt/huxley/client";

    // Client paths are written Windows-style ("HuxleyGame\Content\..."), and the install's folder
    // names do not always match the case the server uses. On Linux both matter, so a path is split
    // on either separator and each part is matched case-insensitively when the exact name is absent.
    public static string Resolve(string root, string relative)
    {
        string path = root;
        foreach (string part in relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            string exact = Path.Combine(path, part);
            if (OperatingSystem.IsWindows() || File.Exists(exact) || Directory.Exists(exact) || !Directory.Exists(path))
            {
                path = exact;
                continue;
            }
            path = Directory.EnumerateFileSystemEntries(path)
                .FirstOrDefault(entry => Path.GetFileName(entry).Equals(part, StringComparison.OrdinalIgnoreCase))
                ?? exact;
        }
        return path;
    }

    public static string Resolve(string relative) => Resolve(Root, relative);

    // Directory searches that ignore case on every platform ("*.war" also finds ".WAR").
    public static readonly EnumerationOptions AnyCase = new()
    {
        RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true
    };

    // A path the game (running under Wine on Linux) can open: Wine maps the Unix root to drive Z:.
    public static string GamePath(string path) =>
        OperatingSystem.IsWindows() ? path : "Z:" + Path.GetFullPath(path).Replace('/', '\\');
}
