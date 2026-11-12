using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;

namespace Lunarite.Storage;

// Per-character quest journal: the possible, acquired and completed lists the client takes
// from the master, plus objective and result progress.
public sealed class QuestStore
{
    private readonly ILogger _logger;

    public sealed class State
    {
        public HashSet<int> Acquired { get; set; } = [];

        public HashSet<int> Completed { get; set; } = [];

        public Dictionary<int, Progress> Progress { get; set; } = [];
    }

    public sealed class Progress
    {
        public byte RallyPoint { get; set; }

        public byte MaxRallyPoint { get; set; }

        public byte Complete { get; set; } = 1; // QUESTCOMPLETE_PLAYING

        [JsonPropertyName("SquadBp")] public int SquadBP { get; set; }

        [JsonPropertyName("Bp")] public int BP { get; set; }

        [JsonPropertyName("Qt")] public int QT { get; set; }

        // The run (instance token and its StartedTicks) whose rally report stored BP/QT; quest
        // completion books the payout against that run's limits.
        public int RunInstance { get; set; }

        public long RunStarted { get; set; }

        public int KilledMonsters { get; set; }

        public int SpawnedMonsters { get; set; }

        public int PlayTime { get; set; }

        public Dictionary<byte, int> WeaponKills { get; set; } = [];

        public Dictionary<int, Objective> Objectives { get; set; } = [];
    }

    public sealed class Objective
    {
        public int Count { get; set; }

        public bool Visible { get; set; }
    }

    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "playerquests.json");
    private readonly object _lock = new();
    private readonly Dictionary<int, State> _states;

    private readonly QuestCatalog _questCatalog;

    private readonly AtomicJson _atomicJson;
    private readonly MultiStoreTransaction _multiStoreTransaction;

    public QuestStore(ILogger<QuestStore> logger, QuestCatalog questCatalog, AtomicJson atomicJson,
        MultiStoreTransaction multiStoreTransaction)
    {
        _logger = logger;
        _questCatalog = questCatalog;
        _atomicJson = atomicJson;
        _multiStoreTransaction = multiStoreTransaction;
        _states = Load();
    }

    private Dictionary<int, State> Load()
    {
        try
        {
            return _atomicJson.Read<Dictionary<int, State>>(_path) ?? [];
        }
        catch (Exception ex) { _logger.LogWarning("[Quests] load failed: {0}", ex.Message); }
        return [];
    }

    // op56 arrives once per enemy death; its kill counter is written at most once per
    // KillSaveDelay instead of re-serializing the whole store on every report. Any other Save
    // in the meantime writes it too.
    internal static readonly TimeSpan KillSaveDelay = TimeSpan.FromSeconds(1);
    private bool _saveQueued;
    internal int SaveCount;

    private void SaveSoon()
    {
        if (_saveQueued) return;
        _saveQueued = true;
        _ = System.Threading.Tasks.Task.Delay(KillSaveDelay).ContinueWith(_ =>
        {
            lock (_lock) if (_saveQueued) Save();
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    // Writes a queued kill save now (host shutdown).
    public void Flush()
    {
        lock (_lock) if (_saveQueued) Save();
    }

    private void Save()
    {
        _saveQueued = false;
        SaveCount++;
        try { _atomicJson.Write(_path, JsonSerializer.Serialize(_states, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[Quests] save failed: {0}", ex.Message); }
    }

    public IReadOnlyList<int> Acquired(int pid)
    {
        lock (_lock) return _states.TryGetValue(pid, out var state) ? state.Acquired.Order().ToList() : [];
    }

    public IReadOnlySet<int> Completed(int pid)
    {
        lock (_lock)
            return _states.TryGetValue(pid, out var state) ? new HashSet<int>(state.Completed) : new HashSet<int>();
    }

    public bool ReadyToComplete(int pid, int questIndex)
    {
        var required = _questCatalog.Get(questIndex)?.Objectives
            .Where(x => x.ObjectiveType == "QuestObjectiveType_Primary").ToArray() ?? [];
        lock (_lock)
            return _states.TryGetValue(pid, out var state)
                && state.Acquired.Contains(questIndex)
                && state.Progress.TryGetValue(questIndex, out var progress)
                && (progress.Complete == 2 // QUESTCOMPLETE_REWARD
                    || (required.Length != 0 && required.All(x => progress.Objectives.TryGetValue(x.Index, out var objective)
                        && objective.Count >= x.CompleteCount)));
    }

    public bool Acquire(int pid, int questIndex)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state)) state = _states[pid] = new State();
            if (state.Completed.Contains(questIndex) || !state.Acquired.Add(questIndex)) return false;
            state.Progress[questIndex] = new Progress();
            Save();
            _logger.LogInformation("[Quests] PID {0} acquired {1}.", pid, questIndex);
            return true;
        }
    }

    public bool Complete(int pid, int questIndex, bool repeatable = false)
    {
        bool completed = false;
        _multiStoreTransaction.Run(() =>
        {
            lock (_lock)
            {
                if (!_states.TryGetValue(pid, out var state) || !state.Acquired.Remove(questIndex)) return;
                state.Progress.Remove(questIndex);
                if (!repeatable) state.Completed.Add(questIndex);
                completed = true;
                Save();
                _logger.LogInformation("[Quests] PID {0} completed {1}{2}.", pid, questIndex,
                    repeatable ? " (repeatable; not added to completion list)" : "");
            }
        });
        return completed;
    }

    public bool Retire(int pid, int questIndex)
    {
        bool retired = false;
        _multiStoreTransaction.Run(() =>
        {
            lock (_lock)
            {
                if (!_states.TryGetValue(pid, out var state) || !state.Acquired.Remove(questIndex)) return;
                state.Progress.Remove(questIndex);
                Save();
                retired = true;
                _logger.LogInformation("[Quests] PID {0} retired {1}.", pid, questIndex);
            }
        });
        return retired;
    }

    public void Reset(int pid)
    {
        lock (_lock) { _states.Remove(pid); Save(); }
    }

    public void Delete(int pid) => Reset(pid);

    // QuestType_Free can be entered again without journal acquisition. Its enemy and terminal
    // weapon counters are absolute within one instance, not cumulative across separate runs.
    // Call only after the next room's admission and entry-item transaction succeed.
    public bool BeginDirectFieldRun(int pid, int questIndex)
    {
        if (pid <= 0 || questIndex <= 0
            || _questCatalog.Get(questIndex)?.QuestType != "QuestType_Free") return false;
        lock (_lock)
        {
            if (_states.TryGetValue(pid, out var state))
            {
                if (state.Acquired.Contains(questIndex)) return false;
                if (state.Progress.Remove(questIndex)) Save();
            }
            return true;
        }
    }

    public Progress GetProgress(int pid, int questIndex)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state) || !state.Progress.TryGetValue(questIndex, out var progress))
                return new Progress();
            return new Progress
            {
                RallyPoint = progress.RallyPoint,
                MaxRallyPoint = progress.MaxRallyPoint,
                Complete = progress.Complete,
                SquadBP = progress.SquadBP,
                BP = progress.BP,
                QT = progress.QT,
                RunInstance = progress.RunInstance,
                RunStarted = progress.RunStarted,
                KilledMonsters = progress.KilledMonsters,
                SpawnedMonsters = progress.SpawnedMonsters,
                PlayTime = progress.PlayTime,
                WeaponKills = (progress.WeaponKills ?? []).ToDictionary(x => x.Key, x => x.Value),
                Objectives = progress.Objectives.ToDictionary(x => x.Key,
                    x => new Objective { Count = x.Value.Count, Visible = x.Value.Visible })
            };
        }
    }

    public bool UpdateObjective(int pid, int questIndex, int objectiveIndex, int count, bool visible)
    {
        var quest = _questCatalog.Get(questIndex);
        var definition = quest?.Objectives.FirstOrDefault(x => x.Index == objectiveIndex);
        var required = quest?.Objectives
            .Where(x => x.ObjectiveType == "QuestObjectiveType_Primary").ToArray() ?? [];
        if (definition == null || count < 0 || count > definition.CompleteCount) return false;
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state) || !state.Acquired.Contains(questIndex)) return false;
            if (!state.Progress.TryGetValue(questIndex, out var progress))
                progress = state.Progress[questIndex] = new Progress();
            progress.Objectives[objectiveIndex] = new Objective { Count = count, Visible = visible };
            if (required.Length != 0)
            {
                bool objectivesComplete = required.All(x => progress.Objectives.TryGetValue(x.Index, out var objective)
                    && objective.Count >= x.CompleteCount);
                if (objectivesComplete)
                    progress.Complete = 2; // QUESTCOMPLETE_REWARD; city/free quests can omit rally reports
                else if (!quest!.UseQuestRoom
                    && quest.QuestType is "QuestType_City" or "QuestType_FreeQuest"
                    && progress.MaxRallyPoint == 0)
                    progress.Complete = 1; // an absolute op55 count can revoke no-room reward readiness
            }
            Save();
            return true;
        }
    }

    public bool RecordEnemyDown(int pid, int questIndex, bool authenticatedDirectFieldRun = false)
    {
        if (pid <= 0 || questIndex <= 0) return false;
        lock (_lock)
        {
            bool directFieldRun = authenticatedDirectFieldRun
                && _questCatalog.Get(questIndex)?.QuestType == "QuestType_Free";
            if (!_states.TryGetValue(pid, out var state))
            {
                if (!directFieldRun) return false;
                state = _states[pid] = new State();
            }
            if (!state.Acquired.Contains(questIndex) && !directFieldRun) return false;
            if (!state.Progress.TryGetValue(questIndex, out var progress))
                progress = state.Progress[questIndex] = new Progress();
            if (progress.KilledMonsters < int.MaxValue) progress.KilledMonsters++;
            SaveSoon();
            return true;
        }
    }

    // SendQuestResult carries four absolute per-weapon-category kill counters. Keep the highest
    // value per category so a duplicate or stale report cannot lower it. Enemy+weapon objectives
    // use the objective report instead, since this block does not identify the enemy type.
    public bool UpdateWeaponKills(int pid, int questIndex,
        IEnumerable<(byte Category, ushort Kills)> weaponUses, bool authenticatedDirectFieldRun = false)
    {
        if (pid <= 0 || questIndex <= 0 || weaponUses == null) return false;
        lock (_lock)
        {
            bool directFieldRun = authenticatedDirectFieldRun
                && _questCatalog.Get(questIndex)?.QuestType == "QuestType_Free";
            if (!_states.TryGetValue(pid, out var state))
            {
                if (!directFieldRun) return false;
                state = _states[pid] = new State();
            }
            if (!state.Acquired.Contains(questIndex) && !directFieldRun) return false;
            if (!state.Progress.TryGetValue(questIndex, out var progress))
                progress = state.Progress[questIndex] = new Progress();
            progress.WeaponKills ??= [];
            bool changed = false;
            foreach (var (category, kills) in weaponUses)
            {
                if (category == 0 || kills == 0) continue;
                int previous = progress.WeaponKills.GetValueOrDefault(category);
                if (kills <= previous) continue;
                progress.WeaponKills[category] = kills;
                changed = true;
            }
            if (changed) Save();
            return true;
        }
    }

    public sealed record ObjectiveUpdate(int QuestIndex, int ObjectiveIndex, int Count);

    // HuxleySubsystem.ResetFreeQuestObjectives seeds FQOType_PickItem from GetItemCount rather
    // than zero. GetItemCount scans INVEN_MAIN only, so accepting a field quest immediately
    // recognizes matching items the character already carries (including QuestItems granted by
    // the same acquisition transaction).
    public IReadOnlyList<ObjectiveUpdate> InitializePickItemObjectives(
        int pid, int questIndex, Func<int, int> mainInventoryCount)
    {
        if (pid <= 0 || mainInventoryCount == null) return [];
        var quest = _questCatalog.Get(questIndex);
        if (quest == null) return [];
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state) || !state.Acquired.Contains(questIndex)) return [];
            if (!state.Progress.TryGetValue(questIndex, out var progress))
                progress = state.Progress[questIndex] = new Progress();
            var required = quest.Objectives
                .Where(x => x.ObjectiveType == "QuestObjectiveType_Primary").ToArray();
            List<ObjectiveUpdate> updates = [];
            foreach (var definition in quest.Objectives.Where(x =>
                x.BattleZoneObjective == "FQOType_PickItem" && x.ExtraParam > 0))
            {
                int count = Math.Clamp(mainInventoryCount(definition.ExtraParam), 0, definition.CompleteCount);
                int oldCount = progress.Objectives.GetValueOrDefault(definition.Index)?.Count ?? 0;
                if (count == oldCount) continue;
                progress.Objectives[definition.Index] = new Objective { Count = count, Visible = true };
                updates.Add(new ObjectiveUpdate(questIndex, definition.Index, count));
            }
            if (required.Length != 0 && required.All(x =>
                    progress.Objectives.TryGetValue(x.Index, out var value)
                    && value.Count >= x.CompleteCount))
                progress.Complete = 2;
            if (updates.Count != 0) Save();
            return updates;
        }
    }

    // The exported inventory callbacks report GetItemCount(ItemIndex), not the inserted delta, so
    // field-objective counts are replaced with the current MAIN total; removal can lower them.
    public IReadOnlyList<ObjectiveUpdate> SynchronizePickItemObjectives(
        int pid, int itemIndex, int mainInventoryCount)
    {
        if (pid <= 0 || itemIndex <= 0 || mainInventoryCount < 0) return [];
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state)) return [];
            List<ObjectiveUpdate> updates = [];
            foreach (int questIndex in state.Acquired)
            {
                var quest = _questCatalog.Get(questIndex);
                if (quest == null) continue;
                var matches = quest.Objectives.Where(x =>
                    x.BattleZoneObjective == "FQOType_PickItem" && x.ExtraParam == itemIndex).ToArray();
                if (matches.Length == 0) continue;
                if (!state.Progress.TryGetValue(questIndex, out var progress))
                    progress = state.Progress[questIndex] = new Progress();
                foreach (var definition in matches)
                {
                    int count = Math.Min(mainInventoryCount, definition.CompleteCount);
                    int oldCount = progress.Objectives.GetValueOrDefault(definition.Index)?.Count ?? 0;
                    if (count == oldCount) continue;
                    bool visible = progress.Objectives.GetValueOrDefault(definition.Index)?.Visible ?? true;
                    progress.Objectives[definition.Index] = new Objective { Count = count, Visible = visible };
                    updates.Add(new ObjectiveUpdate(questIndex, definition.Index, count));
                }
                var required = quest.Objectives
                    .Where(x => x.ObjectiveType == "QuestObjectiveType_Primary").ToArray();
                if (required.Length != 0)
                    progress.Complete = required.All(x => progress.Objectives.TryGetValue(x.Index, out var value)
                        && value.Count >= x.CompleteCount) ? (byte)2 : (byte)1;
            }
            if (updates.Count != 0) Save();
            return updates;
        }
    }

    // Field-quest objectives name their event type and target EntryIndex in HuxleyQuestTable.
    // Server-observed kills/pickups are a fallback; a later op55 report replaces the count.
    public IReadOnlyList<ObjectiveUpdate> RecordFieldEvent(
        int pid, string eventType, int targetEntryIndex, int amount = 1)
    {
        if (pid <= 0 || targetEntryIndex <= 0 || amount <= 0
            || eventType is not ("FQOType_KillEnemy" or "FQOType_PickItem")) return [];
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state)) return [];
            List<ObjectiveUpdate> updates = [];
            foreach (int questIndex in state.Acquired)
            {
                var quest = _questCatalog.Get(questIndex);
                if (quest == null) continue;
                var required = quest.Objectives
                    .Where(x => x.ObjectiveType == "QuestObjectiveType_Primary").ToArray();
                foreach (var definition in quest.Objectives.Where(x =>
                    x.BattleZoneObjective == eventType && x.ExtraParam == targetEntryIndex))
                {
                    if (!state.Progress.TryGetValue(questIndex, out var progress))
                        progress = state.Progress[questIndex] = new Progress();
                    int oldCount = progress.Objectives.GetValueOrDefault(definition.Index)?.Count ?? 0;
                    int count = (int)Math.Min(definition.CompleteCount, (long)oldCount + amount);
                    if (count == oldCount) continue;
                    bool visible = progress.Objectives.GetValueOrDefault(definition.Index)?.Visible ?? true;
                    progress.Objectives[definition.Index] = new Objective { Count = count, Visible = visible };
                    if (required.Length != 0 && required.All(x =>
                            progress.Objectives.TryGetValue(x.Index, out var value)
                            && value.Count >= x.CompleteCount))
                        progress.Complete = 2;
                    updates.Add(new ObjectiveUpdate(questIndex, definition.Index, count));
                }
            }
            if (updates.Count != 0) Save();
            return updates;
        }
    }

    // HuxleyMultiPlayerController maps BQOType_TDM/Radar/PickUp/CTF/DM to
    // BattleQuestObjectiveType_Match with EBZType 1/2/3/4/5. The client normally sends op55 at
    // match end; op114 battle telemetry is an idempotent fallback for when that callback is
    // skipped during transport teardown.
    public IReadOnlyList<ObjectiveUpdate> RecordBattleZoneMatch(int pid, bool simulation, byte gameType,
        bool won = false, int kills = 0)
    {
        // EBZType 1..10 are retail battle modes. Match objectives name only modes 1..5;
        // aggregate Kills/Victorys also count modes 6..10.
        if (gameType is < 1 or > 10 || kills < 0) return [];
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state)) return [];
            List<ObjectiveUpdate> updates = [];
            foreach (int questIndex in state.Acquired)
            {
                var quest = _questCatalog.Get(questIndex);
                if (quest == null || !quest.QuestType.Equals("QuestType_BattleZone", StringComparison.Ordinal))
                    continue;
                var required = quest.Objectives.Where(x => x.ObjectiveType == "QuestObjectiveType_Primary").ToArray();
                foreach (var definition in quest.Objectives)
                {
                    bool typeMatches = definition.BattleZoneObjective switch
                    {
                        "BQOType_TDM" => gameType == 1,
                        "BQOType_Radar" => gameType == 2,
                        "BQOType_PickUp" => gameType == 3,
                        "BQOType_CTF" => gameType == 4,
                        "BQOType_DM" => gameType == 5,
                        "BQOType_AllMatch" => true,
                        "BQOType_Kills" => kills > 0,
                        "BQOType_Victorys" => won,
                        _ => false
                    };
                    bool modeMatches = definition.BattleZoneGameType switch
                    {
                        "BQOGameType_SimulationBattle" => simulation,
                        "BQOGameType_Battle" or "BQOGameType_MassiveBattleZone" => !simulation,
                        "BQOGameType_BattleAll" => true,
                        _ => false
                    };
                    if (!typeMatches || !modeMatches) continue;
                    if (!state.Progress.TryGetValue(questIndex, out var progress))
                        progress = state.Progress[questIndex] = new Progress();
                    int oldCount = progress.Objectives.GetValueOrDefault(definition.Index)?.Count ?? 0;
                    int increment = definition.BattleZoneObjective == "BQOType_Kills" ? kills : 1;
                    int count = Math.Min(definition.CompleteCount, oldCount + increment);
                    if (count == oldCount) continue;
                    bool visible = progress.Objectives.GetValueOrDefault(definition.Index)?.Visible ?? false;
                    progress.Objectives[definition.Index] = new Objective { Count = count, Visible = visible };
                    if (required.Length != 0 && required.All(x => progress.Objectives.TryGetValue(x.Index, out var value)
                            && value.Count >= x.CompleteCount))
                        progress.Complete = 2;
                    updates.Add(new ObjectiveUpdate(questIndex, definition.Index, count));
                }
            }
            if (updates.Count != 0) Save();
            return updates;
        }
    }

    // The same settlement is sent for journal quests (HuxleyQuestGame.QuestAbort,
    // QuestObjectiveFailed, checkpoints); retail credits it at once.
    public static bool IsSettlementReport(byte rallyPoint, byte maxRallyPoint, byte complete) =>
        rallyPoint == 0 && maxRallyPoint == 0 && complete == 0;

    public bool UpdateRally(int pid, int questIndex, byte rallyPoint, byte maxRallyPoint, byte complete,
        int squadBp = 0, int bp = 0, int qt = 0, int killedMonsters = 0, int spawnedMonsters = 0, int playTime = 0,
        int runInstance = 0, long runStarted = 0)
    {
        // ENUM_QUEST_COMPLETE is 0..3. BP is signed because retail subtracts its mission-death
        // penalty from the accumulated result. The other earned values remain counters.
        if (complete > 3 || rallyPoint > maxRallyPoint || squadBp < 0 || qt < 0
            || killedMonsters < 0 || spawnedMonsters < 0 || playTime < 0)
            return false;
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state) || !state.Acquired.Contains(questIndex)) return false;
            if (!state.Progress.TryGetValue(questIndex, out var progress))
                progress = state.Progress[questIndex] = new Progress();
            SetRallyProgress(progress, rallyPoint, maxRallyPoint, complete, squadBp, bp, qt,
                killedMonsters, spawnedMonsters, playTime, runInstance, runStarted);
            Save();
            return true;
        }
    }

    // QuestType_Free runs are not in the journal. The caller authenticates the participant's
    // live instance before recording op54 telemetry.
    public bool UpdateDirectFieldRally(int pid, int questIndex, byte rallyPoint,
        byte maxRallyPoint, byte complete, int squadBp, int bp, int qt, int killedMonsters,
        int spawnedMonsters, int playTime, int runInstance = 0, long runStarted = 0, bool helper = false)
    {
        // A helper (QuestInstanceManager.IsQuestHelper) is paid its kill BP like a field run, on any quest.
        if (pid <= 0 || questIndex <= 0 || (!helper && _questCatalog.Get(questIndex)?.QuestType != "QuestType_Free")
            || complete > 3 || rallyPoint > maxRallyPoint || squadBp < 0 || qt < 0
            || killedMonsters < 0 || spawnedMonsters < 0 || playTime < 0) return false;
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state)) state = _states[pid] = new State();
            if (state.Acquired.Contains(questIndex)) return false;
            if (!state.Progress.TryGetValue(questIndex, out var progress))
                progress = state.Progress[questIndex] = new Progress();
            SetRallyProgress(progress, rallyPoint, maxRallyPoint, complete, squadBp, bp, qt,
                killedMonsters, spawnedMonsters, playTime, runInstance, runStarted);
            Save();
            return true;
        }
    }

    // The run ended without the client's settlement report (its host was lost). Returns the
    // run's BP and QT for crediting and consumes them, as a settlement report does.
    public (int BP, int QT) TakeRunTotals(int pid, int questIndex)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(pid, out var state) || !state.Progress.TryGetValue(questIndex, out var progress))
                return (0, 0);
            var totals = (progress.BP, progress.QT);
            if (totals == (0, 0)) return totals;
            progress.BP = 0;
            progress.QT = 0;
            Save();
            return totals;
        }
    }

    private static void SetRallyProgress(Progress progress, byte rallyPoint, byte maxRallyPoint,
        byte complete, int squadBp, int bp, int qt, int killedMonsters, int spawnedMonsters,
        int playTime, int runInstance, long runStarted)
    {
        // Bp/Qt hold the run total since the last settlement. A checkpoint/abort settlement
        // (rally 0/0, UNKNOWN) is credited by the handler and consumes that total; it never
        // lowers a reached completion state. A zero-valued completion marker such as
        // HuxleyMultiPlayerControllerBase's (1, 1, REWARD, 0, 0) keeps the running total.
        bool settlement = IsSettlementReport(rallyPoint, maxRallyPoint, complete);
        if (settlement)
        {
            progress.BP = 0;
            progress.QT = 0;
        }
        else
        {
            progress.RallyPoint = rallyPoint;
            progress.MaxRallyPoint = maxRallyPoint;
            progress.Complete = complete;
            if (bp != 0 || qt != 0 || complete < 2)
            {
                progress.BP = bp;
                progress.QT = qt;
                progress.RunInstance = runInstance;
                progress.RunStarted = runStarted;
            }
        }
        progress.SquadBP = squadBp;
        // op56 is one report per enemy death. Keep the larger count when a gateway-less
        // instance later sends zero or stale rally telemetry.
        progress.KilledMonsters = Math.Max(progress.KilledMonsters, killedMonsters);
        progress.SpawnedMonsters = spawnedMonsters;
        progress.PlayTime = playTime;
    }
}
