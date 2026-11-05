using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleResultHandler
{
    private static BattleManager _battleManager = null!;
    private static CharacterManager _characterManager = null!;
    private static SimulationManager _simulationManager = null!;
    private static ILogger _logger = null!;
    private static BattleResultStore _battleResultStore = null!;
    private static CharacterStore _characterStore = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestStore _questStore = null!;
    private static ServerEvents _serverEvents = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BattleResultHandler));

        _battleResultStore = serviceProvider.GetRequiredService<BattleResultStore>();
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
        _serverEvents = serviceProvider.GetRequiredService<ServerEvents>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!BattleResultRequest.TryDeserialize(ref reader, out var report)) return false;
        int reporterPid = Game.PidOf(client);
        int bp = report.AwardedLevelBp;
        if (report.Result != 9 || report.EmbeddedPid != report.Pid
            || report.GameType is < 1 or > 10 || bp < 0 || bp > 100_000
            || _characterStore.Get(report.Pid) == null)
        {
            _logger.LogWarning(
                "[BattleResult] rejected result PID {0} from PID {1}: result={2}, embedded={3}, mode={4}, BP={5}.",
                report.Pid, reporterPid, report.Result, report.EmbeddedPid, report.GameType, bp);
            return true;
        }
        // Only the listen host of a Simulation match it just ended (op115) reports results, once
        // per member of that match; anything else would credit BP, QT and Skirmish items at will.
        if (!_simulationManager.TryClaimMatchResult(reporterPid, report.Pid, out int measuredSeconds,
                out ushort skirmishGoal, out int timeLimitSeconds, out int joinedAtSeconds, out bool ranFullTime))
        {
            _logger.LogWarning("[BattleResult] rejected result PID {0} from PID {1}: not the host of a just-finished match with that member, or already settled.",
                report.Pid, reporterPid);
            return true;
        }
        // The claim already applies each member's result once; a content match would also drop a
        // second identical match (an idle loser's result repeats byte for byte).
        _battleResultStore.Add(report, BattleResultStore.SourceLocal, measuredSeconds);
        // A match shorter than a minute pays nothing; its durability is still spent.
        if (measuredSeconds < SimulationManager.SimulationMinRewardSeconds)
        {
            _battleManager.ApplyBattleDurability(report);
            _logger.LogWarning("[BattleResult] PID {0} from PID {1}: match lasted {2} s, under {3} s; no reward.",
                report.Pid, reporterPid, measuredSeconds, SimulationManager.SimulationMinRewardSeconds);
            return true;
        }
        // The host computes BP and QT; credit no more than retail's per-match ceilings for the
        // member's level class: BP weighted by retail GetPlayerPlayTime (full for a member present
        // from the start, even when a score goal ends the match early), QT by the measured time.
        bool pcRoom = _serverEvents.PcRoomActive;
        int bpLimit = SimulationManager.SimulationBPLimit(_playerStore.GetProgress(report.Pid).Level, pcRoom,
            measuredSeconds, timeLimitSeconds, joinedAtSeconds);
        int qtLimit = SimulationManager.SimulationQTLimit(measuredSeconds, pcRoom);
        if (bp > bpLimit || report.QT > qtLimit)
            _logger.LogWarning("[BattleResult] PID {0} from PID {1}: BP {2} / QT {3} over the limit {4} / {5} for {6} s; capped.",
                report.Pid, reporterPid, bp, report.QT, bpLimit, qtLimit, measuredSeconds);
        bp = Math.Min(bp, bpLimit);
        int qt = Math.Min(report.QT, qtLimit);
        var progress = _playerStore.AddReward(report.Pid, 0, bp, 0, 0, qt, out int previousLevel);
        _characterManager.PublishLiveProgression(report.Pid, previousLevel, progress);
        if (QuestManager.QuestRewardLiveRecipient(report.Pid) is { } resultRecipient)
            _characterManager.SendTotalKillDeath(resultRecipient, report.Pid);
        _battleManager.ApplyBattleDurability(report);
        if (skirmishGoal == 0 && ranFullTime)
            _simulationManager.GrantSkirmishRewards(report, progress.Level);
        foreach (var update in _questStore.RecordBattleZoneMatch(report.Pid, true, report.GameType,
                     report.Won, report.Kills))
            _logger.LogInformation("[Quests] PID {0} battle result advanced quest {1} objective {2} to {3}.",
                report.Pid, update.QuestIndex, update.ObjectiveIndex, update.Count);
        _logger.LogInformation("[BattleResult] stored PID {0}, species {1}, result {2}; credited +{3} BP (total {4}, level {5}).",
            report.Pid, report.Species, report.Result, bp, progress.BP, progress.Level);
        return true;
    }
}
