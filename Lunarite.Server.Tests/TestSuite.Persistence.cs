using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void StorePersistence()
    {
        string atomicPath = Path.Combine(AppContext.BaseDirectory, "atomic-test.json");
        _atomicJson.Delete(atomicPath);
        foreach (string suffix in new[] { "", ".tmp", ".bak" })
            File.Delete(Path.Combine(AppContext.BaseDirectory, "transaction-redo.json" + suffix));
        _atomicJson.Write(atomicPath, "{\"generation\":1}");
        _atomicJson.Write(atomicPath, "{\"generation\":2}");
        Check(_sqliteStore.Get("atomic-test.json") == "{\"generation\":2}" && !File.Exists(atomicPath)
            && _atomicJson.Read<Dictionary<string, int>>(atomicPath)?.GetValueOrDefault("generation") == 2,
            "store generations persist as SQLite rows, not JSON files");
        string splitObjectPath = Path.Combine(AppContext.BaseDirectory, "split-object.json");
        string splitArrayPath = Path.Combine(AppContext.BaseDirectory, "split-array.json");
        _atomicJson.Delete(splitObjectPath); _atomicJson.Delete(splitArrayPath);
        _atomicJson.Write(splitObjectPath, "{\"1\":{\"Level\":3},\"2\":{\"Level\":4}}");
        _atomicJson.Write(splitObjectPath, "{\"2\":{\"Level\":5},\"3\":{\"Level\":6}}");
        _atomicJson.Write(splitArrayPath, "[{\"Pid\":7,\"Name\":\"a\"},{\"Pid\":9,\"Name\":\"b\"}]");
        Check(_sqliteStore.Get("split-object.json") == "{\"2\":{\"Level\":5},\"3\":{\"Level\":6}}"
            && SplitRows("store_split_object") == 2 && SplitKeys("store_split_object") == "2,3"
            && SplitKeys("store_split_array") == "7,9"
            && _atomicJson.Read<List<Dictionary<string, System.Text.Json.JsonElement>>>(splitArrayPath)?.Count == 2,
            "object and array stores are kept as one row per entry, keyed by property or element id");
        _atomicJson.Delete(splitObjectPath); _atomicJson.Delete(splitArrayPath);
        Check(SplitRows("store_split_object") == 0 && _sqliteStore.Get("split-object.json") == null,
            "deleting a split store removes its entry rows");
        string legacyImportPath = Path.Combine(AppContext.BaseDirectory, "legacy-import.json");
        _atomicJson.Delete(legacyImportPath);
        File.Delete(legacyImportPath + ".imported"); File.Delete(legacyImportPath + ".bak.imported");
        File.WriteAllText(legacyImportPath, "{\"generation\":7}");
        Check(_atomicJson.Read<Dictionary<string, int>>(legacyImportPath)?.GetValueOrDefault("generation") == 7
            && _sqliteStore.Get("legacy-import.json") == "{\"generation\":7}"
            && !File.Exists(legacyImportPath) && File.Exists(legacyImportPath + ".imported"),
            "a legacy JSON store is imported into SQLite once and renamed to .imported");
        string legacyRepairPath = Path.Combine(AppContext.BaseDirectory, "legacy-repair.json");
        _atomicJson.Delete(legacyRepairPath);
        File.WriteAllText(legacyRepairPath, "{broken");
        File.WriteAllText(legacyRepairPath + ".bak", "{\"generation\":1}");
        Check(_atomicJson.Read<Dictionary<string, int>>(legacyRepairPath)?.GetValueOrDefault("generation") == 1
            && _sqliteStore.Get("legacy-repair.json") == "{\"generation\":1}",
            "importing a corrupt legacy primary takes its valid backup generation");
        string transactionA = Path.Combine(AppContext.BaseDirectory, "transaction-a.json");
        string transactionB = Path.Combine(AppContext.BaseDirectory, "transaction-b.json");
        _multiStoreTransaction.Run(() =>
        {
            _atomicJson.Write(transactionA, "{\"generation\":1}");
            _atomicJson.Write(transactionB, "{\"generation\":2}");
        });
        Check(_sqliteStore.Get("transaction-a.json") == "{\"generation\":1}"
            && _sqliteStore.Get("transaction-b.json") == "{\"generation\":2}"
            && !File.Exists(Path.Combine(AppContext.BaseDirectory, "transaction-redo.json")),
            "cross-store transaction commits every staged generation together");
        try
        {
            _multiStoreTransaction.Run(() =>
            {
                _atomicJson.Write(transactionA, "{\"generation\":9}");
                throw new InvalidOperationException("abort");
            });
        }
        catch (InvalidOperationException) { }
        Check(_sqliteStore.Get("transaction-a.json") == "{\"generation\":1}",
            "an aborted cross-store transaction publishes nothing");
        string redoPath = Path.Combine(AppContext.BaseDirectory, "transaction-redo.json");
        File.WriteAllText(redoPath, System.Text.Json.JsonSerializer.Serialize(
            new MultiStoreTransaction.Journal([
                new MultiStoreTransaction.Write(transactionA, "{\"generation\":3}"),
                new MultiStoreTransaction.Write(transactionB, "{\"generation\":4}")
            ])));
        _multiStoreTransaction.Recover();
        Check(_sqliteStore.Get("transaction-a.json") == "{\"generation\":3}"
            && _sqliteStore.Get("transaction-b.json") == "{\"generation\":4}"
            && !File.Exists(redoPath) && !File.Exists(redoPath + ".bak"),
            "startup completes a redo journal left by the former JSON backend exactly once");
        _atomicJson.Delete(transactionA); _atomicJson.Delete(transactionB);
        string snapshotRoot = Path.Combine(Path.GetTempPath(), "huxley-state-backup-" + Guid.NewGuid().ToString("N"));
        string? snapshot = _stateBackup.CreateSnapshot(AppContext.BaseDirectory, snapshotRoot,
            DateTimeOffset.Parse("2026-09-21T12:34:56Z"));
        var snapshotManifest = snapshot == null ? null
            : System.Text.Json.JsonSerializer.Deserialize<StateBackup.Manifest>(
                File.ReadAllText(Path.Combine(snapshot, "manifest.json")));
        string snapshotDatabase = snapshot == null ? "" : Path.Combine(snapshot, SqliteStore.FileName);
        Check(snapshot != null && Directory.Exists(snapshot)
            && !Directory.Exists(snapshot + ".tmp")
            && File.Exists(snapshotDatabase)
            && snapshotManifest?.Files.Any(x => x.Name == SqliteStore.FileName
                && x.Length == new FileInfo(snapshotDatabase).Length && x.Sha256.Length == 64) == true,
            "state backup publishes an immutable timestamped database snapshot with a hashed manifest");
        Check(snapshot != null && _stateBackup.VerifySnapshot(snapshot),
            "published state snapshot verifies every manifest hash before recovery use");
        _stagedRestore = snapshotRoot + "-staged-recovery";
        Check(snapshot != null && _stateBackup.TryStageRestore(snapshot, _stagedRestore)
            && _stateBackup.VerifySnapshot(_stagedRestore)
            && File.Exists(Path.Combine(_stagedRestore, SqliteStore.FileName)),
            "verified state snapshot stages a complete recovery copy in a new directory");
        Check(snapshot != null && !_stateBackup.TryStageRestore(snapshot, _stagedRestore)
            && !_stateBackup.TryStageRestore(snapshot, Path.Combine(snapshot, "nested-recovery"))
            && _stateBackup.VerifySnapshot(_stagedRestore),
            "state recovery refuses existing and source-nested destinations without altering them");
        if (snapshot != null) File.AppendAllText(snapshotDatabase, "tampered");
        Check(snapshot != null && !_stateBackup.VerifySnapshot(snapshot),
            "state snapshot verification detects a damaged data generation");
        Check(snapshot != null && !_stateBackup.TryStageRestore(snapshot,
                Path.Combine(snapshotRoot, "damaged-recovery"))
            && !Directory.Exists(Path.Combine(snapshotRoot, "damaged-recovery")),
            "state recovery refuses a damaged source without publishing a destination");
        _stateBackup.CreateSnapshot(AppContext.BaseDirectory, snapshotRoot,
            DateTimeOffset.Parse("2026-09-21T13:34:56Z"));
        _stateBackup.CreateSnapshot(AppContext.BaseDirectory, snapshotRoot,
            DateTimeOffset.Parse("2026-09-21T14:34:56Z"));
        _stateBackup.CreateSnapshot(AppContext.BaseDirectory, snapshotRoot,
            DateTimeOffset.Parse("2026-09-21T15:34:56Z"));
        string unrelatedBackupDirectory = Path.Combine(snapshotRoot, "operator-files");
        Directory.CreateDirectory(unrelatedBackupDirectory);
        File.WriteAllText(Path.Combine(unrelatedBackupDirectory, "keep.txt"), "keep");
        Check(_stateBackup.PruneSnapshots(snapshotRoot, 2) == 1
            && Directory.EnumerateDirectories(snapshotRoot)
                .Count(_stateBackup.VerifySnapshot) == 2
            && snapshot != null && Directory.Exists(snapshot)
            && File.Exists(Path.Combine(unrelatedBackupDirectory, "keep.txt")),
            "state backup retention preserves two verified generations and never prunes damaged or unrelated directories");
        Directory.Delete(snapshotRoot, recursive: true);
    }

    private void StaleTransactionSnapshotSkipped()
    {
        string racePath = Path.Combine(AppContext.BaseDirectory, "transaction-race.json");
        _atomicJson.Delete(racePath);
        // A stages a whole-store snapshot; B, on another thread, serializes the newer store state
        // and writes it straight through before A commits.
        _multiStoreTransaction.Run(() =>
        {
            _atomicJson.Write(racePath, "{\"generation\":1}");
            // A dedicated thread: Task.Run(...).Wait() may inline the write on this thread, where
            // it would be staged instead of written through.
            var other = new Thread(() => _atomicJson.Write(racePath, "{\"generation\":2}"));
            other.Start();
            other.Join();
        });
        bool staleSkipped = _sqliteStore.Get("transaction-race.json") == "{\"generation\":2}";
        _atomicJson.Write(racePath, "{\"generation\":3}");
        Check(staleSkipped && _sqliteStore.Get("transaction-race.json") == "{\"generation\":3}",
            "a transaction commit never publishes a staged snapshot over a newer direct write");
        _atomicJson.Delete(racePath);
    }
}
