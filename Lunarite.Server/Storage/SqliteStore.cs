using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

// Authoritative persistence: one SQLite database (huxley.db, WAL, synchronous=FULL). Each store
// keeps its AtomicJson API (a store saves its whole serialized state), but the database holds it
// as one row per top-level entry:
//   - a JSON object store (e.g. playerprogress: PID -> progress) becomes table store_<name> with
//     one row per key;
//   - a JSON array store becomes one row per element, keyed by the elements' unique id field
//     (Pid, UserId, Sequence, ...) or by position when none is unique;
//   - anything else stays a single row in `documents`.
// A save diffs against the stored rows and writes only changed entries; a MultiStoreTransaction
// commits all of its stores in one SQL transaction. store_meta records each split store's shape.
// Views (v_characters, v_accounts, v_progress, v_inventory, v_quests, v_clans) expose typed columns.
// Legacy JSON files are imported at startup and renamed to *.imported.
public sealed class SqliteStore
{
    private readonly ILogger _logger;

    public const string FileName = "huxley.db";
    private static readonly object Gate = new();
    private SqliteConnection? _connection;
    private string? _openPath;
    private static readonly string[] IdFields = ["Pid", "UserId", "Id", "Sequence", "Index", "EntryIndex"];

    public SqliteStore(ILogger<SqliteStore> logger)
    {
        _logger = logger;
    }

    public string DatabasePath =>
        Environment.GetEnvironmentVariable("HUXLEY_DB_PATH") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(AppContext.BaseDirectory, FileName);

    // Store rows are keyed by the path relative to the persistence root (the server directory).
    public static string? KeyFor(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        return full[root.Length..].Replace('\\', '/').ToLowerInvariant();
    }

    // "playerprogress.json" -> store_playerprogress
    public static string TableFor(string key)
    {
        string stem = key.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? key[..^5] : key;
        var name = new StringBuilder("store_");
        foreach (char c in stem) name.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        return name.ToString();
    }

    private SqliteConnection Connection()
    {
        string path = DatabasePath;
        if (_connection != null && _openPath == path) return _connection;
        _connection?.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        connection.Open();
        Execute(connection, null, "PRAGMA journal_mode=WAL;");
        Execute(connection, null, "PRAGMA synchronous=FULL;");
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS documents (
                name TEXT PRIMARY KEY,
                json TEXT NOT NULL,
                updated_utc INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS store_meta (
                name TEXT PRIMARY KEY,
                tbl TEXT NOT NULL,
                kind TEXT NOT NULL,
                id_field TEXT
            );
            """);
        _connection = connection;
        _openPath = path;
        CreateViews(connection);
        return connection;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void EnsureStoreTable(SqliteConnection connection, SqliteTransaction? transaction, string table) =>
        Execute(connection, transaction, $"""
            CREATE TABLE IF NOT EXISTS "{table}" (
                k TEXT PRIMARY KEY,
                ord INTEGER NOT NULL,
                json TEXT NOT NULL,
                updated_utc INTEGER NOT NULL
            );
            """);

    // Read-only convenience views; each needs only its own store table.
    private static void CreateViews(SqliteConnection connection)
    {
        (string View, string Table, string Select)[] views =
        [
            ("v_characters", "store_characters", """
                SELECT CAST(k AS INTEGER) AS pid, json_extract(json,'$.Name') AS name,
                       json_extract(json,'$.OwnerUserId') AS owner_user_id, json_extract(json,'$.Index') AS type_index,
                       json_extract(json,'$.Style') AS style, json_extract(json,'$.CityMap') AS city_map,
                       json_extract(json,'$.CityArea') AS city_area FROM store_characters
                """),
            ("v_accounts", "store_accounts", """
                SELECT CAST(k AS INTEGER) AS user_id, json_extract(json,'$.Login') AS login,
                       json_extract(json,'$.Banned') AS banned, json_extract(json,'$.Cash') AS cash FROM store_accounts
                """),
            ("v_progress", "store_playerprogress", """
                SELECT CAST(k AS INTEGER) AS pid, json_extract(json,'$.Level') AS level, json_extract(json,'$.Rank') AS rank,
                       json_extract(json,'$.Bp') AS bp, json_extract(json,'$.Lp') AS lp, json_extract(json,'$.Money') AS money,
                       json_extract(json,'$.Qt') AS qt, json_extract(json,'$.AMedalsDonated') AS battle_medals_donated,
                       json_extract(json,'$.BMedalsDonated') AS valor_medals_donated FROM store_playerprogress
                """),
            ("v_inventory", "store_playerinventory", """
                SELECT CAST(s.k AS INTEGER) AS pid, json_extract(i.value,'$.Inven') AS inven,
                       json_extract(i.value,'$.Cell') AS cell, json_extract(i.value,'$.ItemId') AS item_id,
                       json_extract(i.value,'$.Count') AS count, json_extract(i.value,'$.Durability') AS durability
                FROM store_playerinventory s, json_each(s.json) i
                """),
            ("v_quests", "store_playerquests", """
                SELECT CAST(k AS INTEGER) AS pid, json_array_length(json,'$.Acquired') AS acquired,
                       json_array_length(json,'$.Completed') AS completed FROM store_playerquests
                """),
            ("v_clans", "store_clans", """
                SELECT CAST(k AS INTEGER) AS clan_id, json_extract(json,'$.Name') AS name, json_extract(json,'$.Mark') AS mark,
                       json_array_length(json,'$.Members') AS members FROM store_clans
                """),
        ];
        foreach (var (view, table, select) in views)
        {
            EnsureStoreTable(connection, null, table);
            Execute(connection, null, $"CREATE VIEW IF NOT EXISTS {view} AS {select};");
        }
    }

    private sealed record Meta(string Table, string Kind, string? IdField);

    private static Meta? ReadMeta(SqliteConnection connection, SqliteTransaction? transaction, string key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT tbl, kind, id_field FROM store_meta WHERE name = $name;";
        command.Parameters.AddWithValue("$name", key);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new Meta(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    private static Dictionary<string, (long Ord, string Json)> ReadRows(SqliteConnection connection,
        SqliteTransaction? transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT k, ord, json FROM \"{table}\";";
        using var reader = command.ExecuteReader();
        var rows = new Dictionary<string, (long, string)>(StringComparer.Ordinal);
        while (reader.Read()) rows[reader.GetString(0)] = (reader.GetInt64(1), reader.GetString(2));
        return rows;
    }

    public string? Get(string key)
    {
        lock (Gate)
        {
            var connection = Connection();
            var meta = ReadMeta(connection, null, key);
            if (meta != null)
            {
                var rows = ReadRows(connection, null, meta.Table).OrderBy(x => x.Value.Ord).ToList();
                var json = new StringBuilder(meta.Kind == "object" ? "{" : "[");
                for (int i = 0; i < rows.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    if (meta.Kind == "object") json.Append(JsonSerializer.Serialize(rows[i].Key)).Append(':');
                    json.Append(rows[i].Value.Json);
                }
                return json.Append(meta.Kind == "object" ? '}' : ']').ToString();
            }
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM documents WHERE name = $name;";
            command.Parameters.AddWithValue("$name", key);
            return command.ExecuteScalar() as string;
        }
    }

    public void Put(string key, string json) => PutAll([(key, json)]);

    // All rows commit or none do.
    public void PutAll(IReadOnlyCollection<(string Key, string Json)> stores)
    {
        if (stores.Count == 0) return;
        lock (Gate)
        {
            var connection = Connection();
            using var transaction = connection.BeginTransaction();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var (key, json) in stores) Write(connection, transaction, key, json, now);
            transaction.Commit();
        }
    }

    private static void Write(SqliteConnection connection, SqliteTransaction transaction, string key, string json, long now)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        List<(string Key, string Json)>? entries = null;
        string kind = "document";
        string? idField = null;
        if (root.ValueKind == JsonValueKind.Object)
        {
            kind = "object";
            entries = root.EnumerateObject().Select(p => (p.Name, p.Value.GetRawText())).ToList();
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            kind = "array";
            var elements = root.EnumerateArray().ToList();
            idField = UniqueIdField(elements);
            entries = elements.Select((e, i) => (idField == null ? i.ToString() : e.GetProperty(idField).GetRawText().Trim('"'),
                e.GetRawText())).ToList();
        }

        if (entries == null)
        {
            DropSplit(connection, transaction, key);
            Execute(connection, transaction, """
                INSERT INTO documents (name, json, updated_utc) VALUES ($name, $json, $now)
                ON CONFLICT(name) DO UPDATE SET json = excluded.json, updated_utc = excluded.updated_utc;
                """, ("$name", key), ("$json", json), ("$now", now));
            return;
        }

        string table = TableFor(key);
        EnsureStoreTable(connection, transaction, table);
        var meta = ReadMeta(connection, transaction, key);
        var existing = meta != null && meta.Table == table ? ReadRows(connection, transaction, table)
            : new Dictionary<string, (long Ord, string Json)>();
        if (meta == null || meta.Table != table) Execute(connection, transaction, $"DELETE FROM \"{table}\";");
        var keep = new HashSet<string>(StringComparer.Ordinal);
        for (int ord = 0; ord < entries.Count; ord++)
        {
            var (entryKey, entryJson) = entries[ord];
            keep.Add(entryKey);
            if (existing.TryGetValue(entryKey, out var old) && old.Ord == ord && old.Json == entryJson) continue;
            Execute(connection, transaction, $"""
                INSERT INTO "{table}" (k, ord, json, updated_utc) VALUES ($k, $ord, $json, $now)
                ON CONFLICT(k) DO UPDATE SET ord = excluded.ord, json = excluded.json, updated_utc = excluded.updated_utc;
                """, ("$k", entryKey), ("$ord", ord), ("$json", entryJson), ("$now", now));
        }
        foreach (string stale in existing.Keys.Where(k => !keep.Contains(k)))
            Execute(connection, transaction, $"DELETE FROM \"{table}\" WHERE k = $k;", ("$k", stale));
        Execute(connection, transaction, """
            INSERT INTO store_meta (name, tbl, kind, id_field) VALUES ($name, $tbl, $kind, $id)
            ON CONFLICT(name) DO UPDATE SET tbl = excluded.tbl, kind = excluded.kind, id_field = excluded.id_field;
            """, ("$name", key), ("$tbl", table), ("$kind", kind), ("$id", idField));
        Execute(connection, transaction, "DELETE FROM documents WHERE name = $name;", ("$name", key));
    }

    // The first candidate field present on every element with distinct scalar values.
    private static string? UniqueIdField(List<JsonElement> elements)
    {
        if (elements.Count == 0 || elements.Any(e => e.ValueKind != JsonValueKind.Object)) return null;
        foreach (string field in IdFields)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            bool ok = true;
            foreach (var element in elements)
            {
                if (!element.TryGetProperty(field, out var value)
                    || value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)
                    || !values.Add(value.GetRawText().Trim('"'))) { ok = false; break; }
            }
            if (ok) return field;
        }
        return null;
    }

    private static void DropSplit(SqliteConnection connection, SqliteTransaction? transaction, string key)
    {
        var meta = ReadMeta(connection, transaction, key);
        if (meta == null) return;
        Execute(connection, transaction, $"DELETE FROM \"{meta.Table}\";");
        Execute(connection, transaction, "DELETE FROM store_meta WHERE name = $name;", ("$name", key));
    }

    public void Delete(string key)
    {
        lock (Gate)
        {
            var connection = Connection();
            using var transaction = connection.BeginTransaction();
            DropSplit(connection, transaction, key);
            Execute(connection, transaction, "DELETE FROM documents WHERE name = $name;", ("$name", key));
            transaction.Commit();
        }
    }

    public IReadOnlyList<string> Names()
    {
        lock (Gate)
        {
            using var command = Connection().CreateCommand();
            command.CommandText = "SELECT name FROM documents UNION SELECT name FROM store_meta ORDER BY name;";
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names;
        }
    }

    // Consistent online copy for snapshots (readers and the WAL are folded into one file).
    public void BackupTo(string destinationFile)
    {
        lock (Gate)
        {
            using var command = Connection().CreateCommand();
            command.CommandText = "VACUUM INTO $path;";
            command.Parameters.AddWithValue("$path", Path.GetFullPath(destinationFile));
            command.ExecuteNonQuery();
        }
    }

    public bool Exists => File.Exists(DatabasePath);

    // One-time migration of every legacy store file in the persistence root that has no row yet.
    // A corrupt primary falls back to its .bak generation; both are renamed to *.imported.
    public int ImportLegacyFiles()
    {
        int imported = 0;
        foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.json"))
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("transaction-redo.json", StringComparison.OrdinalIgnoreCase)) continue;
            string? key = KeyFor(path);
            if (key == null || Get(key) != null) continue;
            string? json = ValidJson(path) ?? ValidJson(path + ".bak");
            if (json == null)
            {
                _logger.LogWarning("[Persistence] {0} and its backup are unreadable; not imported.", name);
                continue;
            }
            Put(key, json);
            foreach (string suffix in new[] { "", ".bak" })
                if (File.Exists(path + suffix)) File.Move(path + suffix, path + suffix + ".imported", overwrite: true);
            imported++;
        }
        if (imported > 0)
            _logger.LogInformation("[Persistence] imported {0} legacy JSON store(s) into {1}.", imported, FileName);
        return imported;
    }

    // Splits stores still held as single `documents` rows (databases from before per-entry tables).
    public int SplitDocuments()
    {
        List<(string Name, string Json)> rows = [];
        lock (Gate)
        {
            using var command = Connection().CreateCommand();
            command.CommandText = "SELECT name, json FROM documents;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        var splittable = rows.Where(r => r.Json.TrimStart() is ['{', ..] or ['[', ..]).ToList();
        if (splittable.Count != 0) PutAll(splittable);
        return splittable.Count;
    }

    private static string? ValidJson(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            string text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Null ? null : text;
        }
        catch (JsonException) { return null; }
    }

    // Tests switch databases between runs; release the handle so the file can be replaced.
    public void Close()
    {
        lock (Gate)
        {
            _connection?.Dispose();
            _connection = null;
            _openPath = null;
            SqliteConnection.ClearAllPools();
        }
    }
}
