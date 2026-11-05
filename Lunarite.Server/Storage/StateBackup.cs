using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunarite.Storage;

// Optional off-process persistence snapshots. Each published directory is immutable: files are
// copied into a temporary sibling first and the directory is renamed only after its manifest is
// flushed. AtomicJson remains responsible for each store's authoritative write generation.
public sealed class StateBackup
{
    private readonly SqliteStore _sqliteStore;

    public StateBackup(SqliteStore sqliteStore)
    {
        _sqliteStore = sqliteStore;
    }

    public sealed record Entry(string Name, long Length, string Sha256);

    public sealed record Manifest(DateTimeOffset CreatedUtc, IReadOnlyList<Entry> Files);

    // Recovery tooling must not mistake a published directory for a usable backup merely
    // because manifest.json exists. Check the complete file set and every recorded digest.
    public bool VerifySnapshot(string snapshotDirectory)
    {
        try
        {
            string directory = Path.GetFullPath(snapshotDirectory);
            string manifestPath = Path.Combine(directory, "manifest.json");
            Manifest? manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
            if (manifest?.Files == null || manifest.Files.Count == 0) return false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in manifest.Files)
            {
                if (entry.Name != Path.GetFileName(entry.Name) || !names.Add(entry.Name)
                    || !(entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || entry.Name.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)
                        || entry.Name.Equals(SqliteStore.FileName, StringComparison.OrdinalIgnoreCase)))
                    return false;
                string path = Path.Combine(directory, entry.Name);
                if (!File.Exists(path) || new FileInfo(path).Length != entry.Length) return false;
                using var stream = File.OpenRead(path);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Sha256,
                        StringComparison.OrdinalIgnoreCase)) return false;
            }
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                .All(path => names.Contains(Path.GetFileName(path)));
        }
        catch (Exception) { return false; }
    }

    public string? CreateSnapshot(string sourceDirectory, string destinationRoot,
        DateTimeOffset? createdUtc = null)
    {
        string source = Path.GetFullPath(sourceDirectory);
        string destination = Path.GetFullPath(destinationRoot);
        var files = Directory.Exists(source)
            ? Directory.EnumerateFiles(source, "*.json", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(source, "*.json.bak", SearchOption.TopDirectoryOnly))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        bool database = _sqliteStore.Exists && string.Equals(Path.GetDirectoryName(_sqliteStore.DatabasePath),
            source.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        if (files.Length == 0 && !database) return null;

        Directory.CreateDirectory(destination);
        DateTimeOffset timestamp = createdUtc ?? DateTimeOffset.UtcNow;
        string baseName = timestamp.UtcDateTime.ToString("yyyyMMddTHHmmss.fffffffZ");
        string final = Path.Combine(destination, baseName);
        for (int suffix = 1; Directory.Exists(final); suffix++)
            final = Path.Combine(destination, $"{baseName}-{suffix}");
        string temporary = final + ".tmp";
        Directory.CreateDirectory(temporary);

        var entries = new List<Entry>(files.Length);
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            string target = Path.Combine(temporary, name);
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 81920, FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            byte[] bytes = File.ReadAllBytes(target);
            entries.Add(new Entry(name, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
        }

        if (database)
        {
            string target = Path.Combine(temporary, SqliteStore.FileName);
            _sqliteStore.BackupTo(target);
            byte[] bytes = File.ReadAllBytes(target);
            entries.Add(new Entry(SqliteStore.FileName, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
        }

        string manifestPath = Path.Combine(temporary, "manifest.json");
        string json = JsonSerializer.Serialize(new Manifest(timestamp.ToUniversalTime(), entries),
            new JsonSerializerOptions { WriteIndented = true });
        using (var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write,
                   FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        Directory.Move(temporary, final);
        return final;
    }

    // Stage a verified recovery copy without replacing a running server's state. The caller
    // must choose a new directory, inspect it, then perform any cutover while services are down.
    public bool TryStageRestore(string snapshotDirectory, string destinationDirectory)
    {
        if (!VerifySnapshot(snapshotDirectory)) return false;
        string source = Path.GetFullPath(snapshotDirectory);
        string destination = Path.GetFullPath(destinationDirectory);
        if (Directory.Exists(destination) || File.Exists(destination)) return false;
        string? parent = Path.GetDirectoryName(destination);
        if (parent == null || !Directory.Exists(parent)) return false;
        // Require a disjoint destination, including on case-insensitive Windows filesystems.
        string sourcePrefix = Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar;
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)) return false;

        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
                File.Copy(file, Path.Combine(temporary, Path.GetFileName(file)), overwrite: false);
            if (!VerifySnapshot(temporary)) return false;
            Directory.Move(temporary, destination);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    public int PruneSnapshots(string destinationRoot, int retain)
    {
        if (retain < 1) throw new ArgumentOutOfRangeException(nameof(retain));
        string destination = Path.GetFullPath(destinationRoot);
        if (!Directory.Exists(destination)) return 0;

        // Only verified publications count toward retention. Keep a damaged snapshot for
        // operator inspection; it must never displace an older recoverable generation.
        string[] snapshots = Directory.EnumerateDirectories(destination)
            .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                && VerifySnapshot(path))
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string snapshot in snapshots.Skip(retain))
            Directory.Delete(snapshot, recursive: true);
        return Math.Max(0, snapshots.Length - retain);
    }
}
