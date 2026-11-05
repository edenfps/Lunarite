using System;
using System.IO;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

// Store generations live in SQLite (SqliteStore) for paths under the persistence root. A legacy
// JSON file found on first read is imported and renamed to *.imported. The file helpers below
// remain for legacy recovery and for paths outside the root: a crash can leave a disposable .tmp
// file, never a truncated JSON file, and File.Replace keeps the previous generation as .bak.
public sealed class AtomicJson
{
    private readonly ILogger _logger;
    private readonly MultiStoreTransaction _multiStoreTransaction;
    private readonly SqliteStore _sqliteStore;

    public AtomicJson(ILogger<AtomicJson> logger, MultiStoreTransaction multiStoreTransaction,
        SqliteStore sqliteStore)
    {
        _logger = logger;
        _multiStoreTransaction = multiStoreTransaction;
        _sqliteStore = sqliteStore;
    }

    public T? Read<T>(string path, JsonSerializerOptions? options = null) where T : class
    {
        string? key = SqliteStore.KeyFor(path);
        if (key == null) return ReadFile<T>(path, options);
        string? stored = _sqliteStore.Get(key);
        if (stored != null)
        {
            T? value = JsonSerializer.Deserialize<T>(stored, options);
            return value ?? throw new InvalidDataException($"Stored generation '{key}' contains null.");
        }
        T? legacy = ReadFile<T>(path, options);
        if (legacy == null) return null;
        // Import exactly the generation that was read (primary, or the recovered backup).
        string legacyJson = File.Exists(path) ? File.ReadAllText(path) : File.ReadAllText(path + ".bak");
        _sqliteStore.Put(key, legacyJson);
        foreach (string suffix in new[] { "", ".bak" })
            if (File.Exists(path + suffix))
                File.Move(path + suffix, path + suffix + ".imported", overwrite: true);
        _logger.LogInformation("[Persistence] imported {0} into {1}.", Path.GetFileName(path), SqliteStore.FileName);
        return legacy;
    }

    // Removes a store generation (tests and resets).
    public void Delete(string path)
    {
        if (SqliteStore.KeyFor(path) is { } key) _sqliteStore.Delete(key);
        foreach (string suffix in new[] { "", ".bak", ".tmp" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }

    private T? ReadFile<T>(string path, JsonSerializerOptions? options) where T : class
    {
        Exception? primaryError = null;
        if (File.Exists(path))
        {
            try
            {
                T? primary = JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
                if (primary == null) throw new JsonException("The primary JSON generation contains null.");
                return primary;
            }
            catch (Exception ex) { primaryError = ex; }
        }

        string backup = path + ".bak";
        if (File.Exists(backup))
        {
            try
            {
                T? recovered = JsonSerializer.Deserialize<T>(File.ReadAllText(backup), options);
                if (recovered != null)
                {
                    RepairPrimary(path, backup);
                    _logger.LogWarning("[Persistence] recovered {0} from its previous atomic generation.",
                        Path.GetFileName(path));
                    return recovered;
                }
                throw new JsonException("The backup JSON generation contains null.");
            }
            catch (Exception backupError)
            {
                if (primaryError != null)
                    throw new InvalidDataException($"Both '{path}' and its backup are unreadable.",
                        new AggregateException(primaryError, backupError));
                throw;
            }
        }

        if (primaryError != null)
            throw new InvalidDataException($"'{path}' is unreadable and has no valid backup.", primaryError);
        return null;
    }

    // Recovery must not call Write(): File.Replace would rotate the corrupt primary into `.bak`
    // and destroy the only known-good generation. Publish an exact flushed copy while leaving the
    // validated backup untouched, so another startup failure still has a recovery source.
    private static void RepairPrimary(string path, string backup)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"No directory for persistence path '{path}'.");
        Directory.CreateDirectory(directory);
        string temporary = fullPath + ".recovery.tmp";
        try
        {
            byte[] bytes = File.ReadAllBytes(backup);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null, ignoreMetadataErrors: true);
            else File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Write(string path, string json)
    {
        // Callers write under their store lock right after serializing, so the sequence orders
        // generations of one path by when they were serialized.
        long sequence = MultiStoreTransaction.NextSequence();
        if (_multiStoreTransaction.TryStage(path, sequence, json)) return;
        _multiStoreTransaction.PublishDirect(path, sequence, () => Publish(path, json));
    }

    internal void Publish(string path, string json)
    {
        if (SqliteStore.KeyFor(path) is { } key) _sqliteStore.Put(key, json);
        else WriteDirect(path, json);
    }

    internal static void WriteDirect(string path, string json)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"No directory for persistence path '{path}'.");
        Directory.CreateDirectory(directory);
        string temporary = fullPath + ".tmp";
        string backup = fullPath + ".bak";

        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath)) File.Replace(temporary, fullPath, backup, ignoreMetadataErrors: true);
            else File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
