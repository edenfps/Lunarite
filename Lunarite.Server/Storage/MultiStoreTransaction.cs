using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

// Gameplay mutations spanning several stores. Store Save methods serialize their complete state;
// inside Run those generations are staged and committed as one SQLite transaction. Recover()
// still completes a redo journal left behind by the former JSON file backend.
// Every generation takes a sequence number when it is written (under its store's lock, right after
// serializing), and a path only ever publishes a newer sequence. A direct write from another thread
// can land between a staged snapshot and its commit; the commit then skips that stale snapshot,
// since the direct write serialized later and already holds this transaction's change.
public sealed class MultiStoreTransaction
{
    private readonly ILogger _logger;
    private readonly SqliteStore _sqliteStore;

    public sealed record Write(string Path, string Json);

    public sealed record Journal(IReadOnlyList<Write> Writes);

    private static readonly object Gate = new();
    // The transaction a thread is inside, if any; other threads write straight through.
    [ThreadStatic] private static Dictionary<string, (long Sequence, string Json)>? _staged;
    private static readonly object PublishGate = new();
    private static readonly Dictionary<string, long> Published = new(StringComparer.OrdinalIgnoreCase);
    private static long _sequence;

    public MultiStoreTransaction(ILogger<MultiStoreTransaction> logger, SqliteStore sqliteStore)
    {
        _logger = logger;
        _sqliteStore = sqliteStore;
    }

    private static string JournalPath => Path.Combine(AppContext.BaseDirectory, "transaction-redo.json");

    public void Recover()
    {
        lock (Gate)
        {
            if (!File.Exists(JournalPath) && !File.Exists(JournalPath + ".bak")) return;
            var journal = JsonSerializer.Deserialize<Journal>(File.Exists(JournalPath)
                ? File.ReadAllText(JournalPath) : File.ReadAllText(JournalPath + ".bak"));
            if (journal == null) { DeleteJournal(); return; }
            Publish(journal);
            DeleteJournal();
            _logger.LogWarning("[Persistence] replayed {0} pending cross-store generation(s).", journal.Writes.Count);
        }
    }

    public void Run(Action mutation)
    {
        lock (Gate)
        {
            if (_staged != null) { mutation(); return; }
            _staged = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                mutation();
                if (_staged.Count == 0) return;
                lock (PublishGate)
                {
                    var current = _staged.Where(x => IsNewest(x.Key, x.Value.Sequence))
                        .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
                    Publish(new Journal(current.Select(x => new Write(x.Key, x.Value.Json)).ToArray()));
                    foreach (var write in current) Published[write.Key] = write.Value.Sequence;
                }
            }
            finally { _staged = null; }
        }
    }

    internal static long NextSequence() => Interlocked.Increment(ref _sequence);

    internal bool TryStage(string path, long sequence, string json)
    {
        if (_staged == null) return false;
        _staged[ValidatePath(path)] = (sequence, json);
        return true;
    }

    // A write outside any transaction; publish runs only if no newer generation of path has.
    internal void PublishDirect(string path, long sequence, Action publish)
    {
        string key = Path.GetFullPath(path);
        lock (PublishGate)
        {
            if (!IsNewest(key, sequence)) return;
            publish();
            Published[key] = sequence;
        }
    }

    private static bool IsNewest(string fullPath, long sequence) =>
        !Published.TryGetValue(fullPath, out long published) || sequence > published;

    private void Publish(Journal journal)
    {
        var rows = new List<(string Key, string Json)>();
        foreach (var write in journal.Writes)
        {
            string path = ValidatePath(write.Path);
            if (SqliteStore.KeyFor(path) is { } key) rows.Add((key, write.Json));
            else AtomicJson.WriteDirect(path, write.Json);
        }
        _sqliteStore.PutAll(rows);
    }

    private static string ValidatePath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Transaction path is outside the persistence root: '{path}'.");
        return full;
    }

    private static void DeleteJournal()
    {
        if (File.Exists(JournalPath)) File.Delete(JournalPath);
        if (File.Exists(JournalPath + ".bak")) File.Delete(JournalPath + ".bak");
    }
}
