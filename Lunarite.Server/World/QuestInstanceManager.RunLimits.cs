using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Storage;

namespace Lunarite.World;

// Bounds on what a listen host reports. The P2P host is authoritative (retail too), so these only
// cap values a legitimate host never reaches; anything above them is dropped instead of granted.
// A run is one instance token plus its StartedTicks, so a new room starts fresh run limits; a
// replaced run's limits stay until RunLimitsKept after its start, so quest completion (op45)
// can book its stored total against them. The hourly budgets are per character and survive new
// rooms, so opening and leaving free field missions in a loop does not reset them. Each refills
// continuously at its hourly amount. BP allowances scale with the run's quest (the
// RecommendLevel of the quest QuestCreate opened), since RecommendLevel is not an entry gate.
public sealed partial class QuestInstanceManager
{
    // op56 is one report per enemy death (HuxleyQuestGame.Killed -> HuxleyEnemyCharacter.RequestItem).
    // Token bucket: a burst of 64 kills (more than any one spawn wave a squad can clear at once)
    // refilled at 4 kills/s (a full MaxPlayers=4 squad each killing one enemy every second, kept up
    // for the whole run). Kills beyond it get an empty op59, which is a valid retail roll.
    internal const int EnemyDownBurst = 64;
    internal const double EnemyDownPerSecond = 4;
    // The listen host reports every kill in its world, its guests' too. 3,600 an hour is one kill
    // every second for a whole hour of loading, travel and city time included: a squad clearing
    // maps nonstop averages well under that (a field run's waves are tens of enemies, then
    // walking to the next rally point).
    internal const int EnemyDownsPerHour = 3600;
    // op57 comes from HuxleySeqAct_SpawnItem only: the supplied client's 68 quest packages hold 491
    // explicit GroupIndex actions plus 16 variable-bound ones (docs/COMPLETION.md, "Next gates"),
    // so no single run can fire more than 512 of them, and no hour of runs twice that.
    internal const int ScriptedDropsPerRun = 512;
    internal const int ScriptedDropsPerHour = 2 * ScriptedDropsPerRun;
    // Settlement QT (HuxleyQuestGame/HuxleyFieldQuestGame.ComputePlayerBattleResult): the run's
    // TotalQuestPCr only grows by durability lost x BattleAquiredQTParam[class], and AcquiredQT
    // never exceeds it. Lost durability is at most 100 (0..100 scale) on each of the 4 WEAPON +
    // 5 ARMOR slots; the largest BattleAquiredQTParam in DefaultGame.ini is 101; the PC-room bonus
    // adds 50% (HuxleyBattleGame PCROOM_BONUS 0.5, EVENT_BONUS 0). An hour may wear the whole
    // gear set out twice, which already costs more QT to repair than it pays.
    internal const long RunQtCeiling = 9L * 100 * 101 * 3 / 2;
    internal const long QtPerHour = 2 * RunQtCeiling;
    // Settlement BP is kill BP (enemy table XP x GetKillBPByEnemyLevel, at most 1 + 0.05 x 45
    // level gap = 3.25), the squad bonus (KillSquadBonusParam 0.2 x 4 + 1 = 1.8 of that) and the
    // PC-room 50%: under 10x the accepted kills' table XP.
    internal const int KillBpFactor = 10;
    // op56 accepts only enemies of the quest's tier: table EnemyLevel at most RecommendLevel + 11.
    // The cooked-map census (MapEnemyExtractor over the US-client client's 4,220 .war maps: every
    // EnemyID factory of each quest map and of the packages it streams, so every difficulty and
    // boss) finds no quest enemy more than 11 levels above its quest: FQ-840011 (RecommendLevel 8)
    // streams level-19 9051910 and FQ-830001 (20) level-30 9123071; 9 quests reach +4 to +11.
    // Difficulty does not change which enemies spawn, only GetKillBPByEnemyLevel
    // (DifficultyLevelAmount 0/5/8). Lower-level enemies are not limited: their kills give less
    // BP, and high-tier maps stream level-1 enemies (FQ-846201, RecommendLevel 33).
    internal const int QuestEnemyLevelMargin = 11;
    // The BP allowances use the lower of the quest's RecommendLevel and the character's own level
    // plus that margin: several high-tier free missions (FQ-846203, RecommendLevel 35; Q-830009,
    // 42) have no entry condition, so a fresh character could otherwise open one and collect the
    // level 30 tier's allowance. A low-level character carried by a party still earns the BP of
    // enemies up to eleven levels above it.
    internal static int AllowanceLevel(int recommendLevel, int characterLevel) =>
        Math.Min(Math.Max(0, recommendLevel), Math.Max(1, characterLevel) + QuestEnemyLevelMargin);
    // Kill BP per run is also capped at four level spans (LevelSpanBp) at the top of the quest's
    // band, RecommendLevel + QuestEnemyLevelMargin: 32,912 BP for a RecommendLevel 1 mission,
    // 236,904 for the level 30 tier. On every quest the census covers, a hundred kills of its
    // map's largest non-boss XP at the hardest difficulty (rate 1.4) with the PC-room bonus earn
    // at most 15% of it, while forged kills of the band's bosses cannot take a character to level
    // 30 in one run. Eight runs' worth an hour, booked as the fraction of its run's cap each
    // credit uses, bounds rooms opened and settled in a loop.
    internal const int KillBpSpansPerRun = 4;
    internal const double KillRunsPerHour = 8;
    // On top of kill BP a run may carry Kismet HuxleySeqAct_AddXp awards (StockBP; the only other
    // source of quest LevelBP). The census finds them in five maps: 51 BP in the level 30 Outback
    // missions (FQ-823031/873031, Q-823015/873015) and five 17,530-25,480 awards in Q-SEC1, which
    // no quest in the table opens. A run's allowance is one level span at its RecommendLevel
    // (112 BP at level 1, 59,226 at 29) plus the quest table's own BP reward, so a zero-kill run
    // of a low-level mission cannot raise a fresh character by more than a level. Four runs'
    // worth an hour bounds a script that opens and settles rooms in a loop without kills.
    internal const double BonusRunsPerHour = 4;
    // A replaced run's limits are dropped once this long has passed since it started.
    internal static readonly TimeSpan RunLimitsKept = TimeSpan.FromHours(6);
    // op60 for another participant (the host reporting a guest): HuxleyQuestGame takes
    // DurabilityDecParam + DurabilityPenaltyAmount (1) per death off each item per result. Allow
    // the quest's DurabilityDecParam plus 10 deaths per result and half the 0..100 scale per run,
    // so one host's run can never break a guest's gear. Excess wear is not applied.
    internal const int OtherParticipantDeathWear = 10;
    internal const int OtherParticipantWearPerRun = 50;
    // A negative settlement BP is the mission-death penalty (HuxleyQuestGame.ProcessPlayerKilled):
    // (LevelupGoalBP - PrevLevelupGoalBP) x BPPenaltyRatio[difficulty] per death, at most 0.00845
    // (Default__HuxleyQuestGame, difficulty 2), none at level 30. Another participant's report
    // may take at most 10 deaths' worth per run and 30 an hour; the reporter's own passes as sent.
    internal const double MaxBpPenaltyRatio = 0.00845;
    internal const int OtherParticipantDeathsPerRun = 10;
    internal const int OtherParticipantDeathsPerHour = 30;

    // BP between the level's threshold and the previous one (HuxleyMultiPlayerReplicationInfo
    // ComputePlayerLevelupGoalBP), the level clamped to 1..29.
    internal static long LevelSpanBp(int level)
    {
        level = Math.Clamp(level, 1, 29);
        return PlayerStore.ComputePlayerLevelupGoalBP(level)
            - (level == 1 ? 0 : PlayerStore.ComputePlayerLevelupGoalBP(level - 1));
    }

    internal static bool EnemyFitsQuest(int enemyLevel, int recommendLevel) =>
        enemyLevel <= Math.Max(0, recommendLevel) + QuestEnemyLevelMargin;

    internal static long RunKillBpCeiling(int recommendLevel) =>
        KillBpSpansPerRun * LevelSpanBp(Math.Max(0, recommendLevel) + QuestEnemyLevelMargin);

    internal static long RunBonusBp(int recommendLevel) => LevelSpanBp(recommendLevel);

    private sealed class RunLimits
    {
        public long Run;
        public int RecommendLevel;
        public double KillTokens = EnemyDownBurst;
        public long RefilledTicks;
        public long KillXp;
        public int ScriptedDrops;
        public readonly Dictionary<int, Credit> Credited = [];
        public readonly Dictionary<int, int> OtherWear = [];
        // op116: guest PID -> belt cell 0 stack seen at its first reduction this run, and reductions.
        public readonly Dictionary<int, (int Brought, int Reduced)> BeltReductions = [];
    }

    private sealed class Credit
    {
        public long KillBp, BonusBp, Qt;
        public int OtherDeaths;
    }

    // Per-character hourly budgets, refilled continuously. KillRuns and BonusRuns count runs'
    // worth of each run's own BP allowance.
    private sealed class HourlyBudget
    {
        public long RefilledTicks;
        public double Kills = EnemyDownsPerHour, ScriptedDrops = ScriptedDropsPerHour, Qt = QtPerHour,
            KillRuns = KillRunsPerHour, BonusRuns = BonusRunsPerHour, OtherDeaths = OtherParticipantDeathsPerHour;

        public void Refill(long now)
        {
            double hours = Math.Max(0, now - RefilledTicks) / (double)TimeSpan.TicksPerHour;
            RefilledTicks = Math.Max(RefilledTicks, now);
            Kills = Math.Min(EnemyDownsPerHour, Kills + hours * EnemyDownsPerHour);
            ScriptedDrops = Math.Min(ScriptedDropsPerHour, ScriptedDrops + hours * ScriptedDropsPerHour);
            Qt = Math.Min(QtPerHour, Qt + hours * QtPerHour);
            KillRuns = Math.Min(KillRunsPerHour, KillRuns + hours * KillRunsPerHour);
            BonusRuns = Math.Min(BonusRunsPerHour, BonusRuns + hours * BonusRunsPerHour);
            OtherDeaths = Math.Min(OtherParticipantDeathsPerHour, OtherDeaths + hours * OtherParticipantDeathsPerHour);
        }
    }

    private readonly ConcurrentDictionary<(int Token, long Run), RunLimits> _runLimits = new();
    private readonly Dictionary<int, HourlyBudget> _hourlyBudgets = [];

    // Host PID -> the quest its current instance runs (QuestCreate).
    private readonly ConcurrentDictionary<int, int> _questInstanceQuests = new();

    private RunLimits LimitsFor(int instanceToken, long now) => LimitsFor(instanceToken, StartedTicks(instanceToken), now);

    // fallbackQuest sets the quest of a run that is not the token's current one.
    private RunLimits LimitsFor(int instanceToken, long run, long now, int fallbackQuest = 0)
    {
        if (_runLimits.TryGetValue((instanceToken, run), out var limits)) return limits;
        long expired = now - RunLimitsKept.Ticks;
        foreach (var key in _runLimits.Keys.Where(key => key.Run > 0 && key.Run < expired
            && StartedTicks(key.Token) != key.Run).ToList())
            _runLimits.TryRemove(key, out _);
        int quest = run != 0 && run == StartedTicks(instanceToken)
            ? _questInstanceQuests.GetValueOrDefault(instanceToken) : fallbackQuest;
        limits = new RunLimits { Run = run, RefilledTicks = now,
            RecommendLevel = _questCatalog.Get(quest)?.RecommendLevel ?? 0 };
        _runLimits[(instanceToken, run)] = limits;
        return limits;
    }

    // The RecommendLevel of the quest an instance token's current run opened (0 when unknown).
    internal int RunRecommendLevel(int instanceToken) =>
        _questCatalog.Get(_questInstanceQuests.GetValueOrDefault(instanceToken))?.RecommendLevel ?? 0;

    private HourlyBudget BudgetFor(int pid, long now)
    {
        if (!_hourlyBudgets.TryGetValue(pid, out var budget))
            _hourlyBudgets[pid] = budget = new HourlyBudget { RefilledTicks = now };
        budget.Refill(now);
        return budget;
    }

    // Takes one op56 token for the run and one from the reporter's hour, and adds the enemy's
    // table XP to the run's kill BP allowance.
    internal bool TryAcceptEnemyDown(int instanceToken, int reporterPid, int enemyXp, long? nowTicks = null)
    {
        if (instanceToken <= 0 || reporterPid <= 0) return false;
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        lock (_runLimits)
        {
            var limits = LimitsFor(instanceToken, now);
            double elapsed = Math.Max(0, now - limits.RefilledTicks) / (double)TimeSpan.TicksPerSecond;
            limits.KillTokens = Math.Min(EnemyDownBurst, limits.KillTokens + elapsed * EnemyDownPerSecond);
            limits.RefilledTicks = Math.Max(limits.RefilledTicks, now);
            var budget = BudgetFor(reporterPid, now);
            if (limits.KillTokens < 1 || budget.Kills < 1) return false;
            limits.KillTokens--;
            budget.Kills--;
            limits.KillXp += Math.Max(0, enemyXp);
            return true;
        }
    }

    internal bool TryAcceptScriptedDrop(int instanceToken, int reporterPid, long? nowTicks = null)
    {
        if (instanceToken <= 0 || reporterPid <= 0) return false;
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        lock (_runLimits)
        {
            var limits = LimitsFor(instanceToken, now);
            var budget = BudgetFor(reporterPid, now);
            if (limits.ScriptedDrops >= ScriptedDropsPerRun || budget.ScriptedDrops < 1) return false;
            limits.ScriptedDrops++;
            budget.ScriptedDrops--;
            return true;
        }
    }

    // BP one death costs at this level (HuxleyQuestGame.ProcessPlayerKilled at difficulty 2).
    internal static long DeathPenaltyBp(int level)
    {
        if (level >= 30) return 0;
        long previous = level <= 1 ? 0 : PlayerStore.ComputePlayerLevelupGoalBP(level - 1);
        return (long)((PlayerStore.ComputePlayerLevelupGoalBP(level) - previous) * MaxBpPenaltyRatio);
    }

    // Clamps a settlement's BP/QT credit to what is left of the participant's run allowance and
    // hourly budget, and books it. Positive BP is the run's kill BP (accepted kills' table XP x
    // KillBpFactor, at most RunKillBpCeiling) plus RunBonusBp and the quest table's BP reward,
    // both parts also limited by the hour. A negative BP from another participant's report is
    // limited to the death penalties above; the reporter's own passes unchanged
    // (CanApplyMissionBp floors it). book: false only previews what would be granted (a stored
    // running total).
    internal (int BP, int QT) ClampRunReward(int instanceToken, int pid, int bp, int qt,
        bool otherParticipant = false, long questRewardBp = 0, bool book = true, long? nowTicks = null)
    {
        if (instanceToken <= 0 || pid <= 0) return (otherParticipant ? 0 : Math.Min(bp, 0), 0);
        int level = bp < 0 && otherParticipant ? PlayerStore.LevelForBP(_playerStore.GetProgress(pid).BP) : 0;
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        lock (_runLimits)
            return ClampRunReward(LimitsFor(instanceToken, now), pid, bp, qt, otherParticipant, level,
                questRewardBp, book, now);
    }

    // Quest completion (op45) pays the running total a rally report stored, booked against the run
    // that stored it (QuestStore.Progress.RunInstance/RunStarted) as a settlement would be. A total
    // stored before runs were recorded, or whose run's limits are gone, has no kill BP left, only
    // the completed quest's bonus allowance.
    internal (int BP, int QT) ClampStoredRunReward(int instanceToken, long runStarted, int pid, int questIndex,
        int bp, int qt, long questRewardBp, long? nowTicks = null)
    {
        if (pid <= 0) return (Math.Min(bp, 0), 0);
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        // A total without a recorded run books against one pseudo-run per quest.
        var (token, run) = instanceToken > 0 ? (instanceToken, runStarted) : (0, -(long)questIndex);
        lock (_runLimits)
            return ClampRunReward(LimitsFor(token, run, now, questIndex), pid, bp, qt,
                otherParticipant: false, level: 0, questRewardBp, book: true, now);
    }

    private (int BP, int QT) ClampRunReward(RunLimits limits, int pid, int bp, int qt, bool otherParticipant,
        int level, long questRewardBp, bool book, long now)
    {
        {
            var budget = BudgetFor(pid, now);
            var credit = limits.Credited.GetValueOrDefault(pid) ?? new Credit();
            long killBp = 0, bonusBp = 0, lostBp = 0;
            int deaths = 0;
            int allowanceLevel = AllowanceLevel(limits.RecommendLevel,
                PlayerStore.LevelForBP(_playerStore.GetProgress(pid).BP));
            long killCeiling = RunKillBpCeiling(allowanceLevel);
            long bonusAllowance = RunBonusBp(allowanceLevel) + Math.Max(0, questRewardBp);
            if (bp > 0)
            {
                long killLeft = Math.Min(Math.Min(limits.KillXp * KillBpFactor, killCeiling) - credit.KillBp,
                    (long)Math.Floor(budget.KillRuns * killCeiling));
                killBp = Math.Clamp(killLeft, 0, bp);
                long bonusLeft = Math.Min(bonusAllowance - credit.BonusBp,
                    (long)Math.Floor(budget.BonusRuns * bonusAllowance));
                bonusBp = Math.Clamp(bonusLeft, 0, bp - killBp);
            }
            else if (bp < 0 && !otherParticipant) lostBp = -(long)bp;
            else if (bp < 0)
            {
                long perDeath = DeathPenaltyBp(level);
                int deathsLeft = Math.Min(OtherParticipantDeathsPerRun - credit.OtherDeaths,
                    (int)Math.Floor(budget.OtherDeaths));
                if (perDeath > 0 && deathsLeft > 0)
                {
                    lostBp = Math.Min(-(long)bp, deathsLeft * perDeath);
                    deaths = (int)((lostBp + perDeath - 1) / perDeath);
                }
            }
            long grantedQt = Math.Clamp(Math.Min(RunQtCeiling - credit.Qt, (long)Math.Floor(budget.Qt)), 0, Math.Max(0, qt));
            if (book)
            {
                credit.KillBp += killBp;
                credit.BonusBp += bonusBp;
                credit.Qt += grantedQt;
                if (otherParticipant) credit.OtherDeaths += deaths;
                limits.Credited[pid] = credit;
                budget.KillRuns -= killBp / (double)killCeiling;
                budget.BonusRuns -= bonusBp / (double)bonusAllowance;
                budget.Qt -= grantedQt;
                if (otherParticipant) budget.OtherDeaths -= deaths;
            }
            return ((int)(killBp + bonusBp - lostBp), (int)grantedQt);
        }
    }

    // op116 for a guest: at most the belt cell 0 stack the guest had at its first reduction this
    // run, so a host cannot also take what the guest gains during the mission.
    internal bool TryAcceptBeltReduction(int instanceToken, int pid, int beltCount)
    {
        if (instanceToken <= 0 || pid <= 0) return false;
        lock (_runLimits)
        {
            var limits = LimitsFor(instanceToken, DateTime.UtcNow.Ticks);
            var (brought, reduced) = limits.BeltReductions.TryGetValue(pid, out var seen) ? seen : (Math.Max(0, beltCount), 0);
            if (reduced >= brought) { limits.BeltReductions[pid] = (brought, reduced); return false; }
            limits.BeltReductions[pid] = (brought, reduced + 1);
            return true;
        }
    }

    // Wear the host may apply to another participant's gear with one op60.
    internal int ClampOtherParticipantWear(int instanceToken, int pid, int wear, int durabilityDecParam)
    {
        if (wear <= 0) return 0;
        if (instanceToken <= 0 || pid <= 0) return 0;
        lock (_runLimits)
        {
            var limits = LimitsFor(instanceToken, DateTime.UtcNow.Ticks);
            int used = limits.OtherWear.GetValueOrDefault(pid);
            int allowed = Math.Min(Math.Min(wear, Math.Max(0, durabilityDecParam) + OtherParticipantDeathWear),
                Math.Max(0, OtherParticipantWearPerRun - used));
            limits.OtherWear[pid] = used + allowed;
            return allowed;
        }
    }
}
