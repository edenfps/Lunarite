using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleBackendResultHandler
{
    private static BattleServerManager _battleServerManager = null!;
    private static BattleFront _battleFront = null!;
    private static BattleZoneCatalog _battleZoneCatalog = null!;
    private static BattleManager _battleManager = null!;
    private static CharacterManager _characterManager = null!;
    private static ILogger _logger = null!;
    private static BattleResultStore _battleResultStore = null!;
    private static CharacterStore _characterStore = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BattleBackendResultHandler));

        _battleResultStore = serviceProvider.GetRequiredService<BattleResultStore>();
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _battleFront = serviceProvider.GetRequiredService<BattleFront>();
        _battleZoneCatalog = serviceProvider.GetRequiredService<BattleZoneCatalog>();
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!_battleServerManager.IsServerLink(client.Id))
        {
            _logger.LogWarning("[BattleBackend] rejected result from unverified connection {0}.", client.Id);
            return true;
        }
        if (!BattleResultRequest.TryDeserialize(ref reader, out var report)) return false;
        int bp = report.AwardedLevelBp;
        if (report.Result != 9 || report.EmbeddedPid != report.Pid || report.GameType is < 1 or > 10
            || bp < 0 || bp > 100_000 || _characterStore.Get(report.Pid) == null)
        {
            _logger.LogWarning("[BattleBackend] rejected invalid result PID {0}, embedded {1}, mode {2}, BP {3}.",
                report.Pid, report.EmbeddedPid, report.GameType, bp);
            return true;
        }
        if (!_battleServerManager.IsLinkPlayer(client.Id, report.Pid))
        {
            _logger.LogWarning("[BattleBackend] rejected result for PID {0}: not a player of this battle server.",
                report.Pid);
            return true;
        }
        if (!_battleResultStore.TryAddUnique(report, TimeSpan.FromMinutes(5),
            BattleResultStore.SourceBattlefield))
        {
            _logger.LogWarning("[BattleBackend] suppressed replayed result for PID {0}.", report.Pid);
            return true;
        }
        var resultZone = _battleServerManager.TryGetServer(client.Id, out var resultServer)
            ? _battleZoneCatalog.GetByServerId(resultServer.Index) : null;
        // A winner's report from a war-front battleground decides the map (later reports of the same
        // match fall inside the battleground's recess and are ignored by BattleFront).
        if (report.Won && resultZone is { OnFront: true }
            && _battleFront.RecordResult(resultZone.EntryIndex, report.Species) is { } outcome)
        {
            _battleManager.PublishBattleFrontOutcome(outcome);
            client.SendPacket(new BattleBackendOccupation { Species = outcome.ToSpecies });
        }
        var progress = _playerStore.AddReward(report.Pid, 0, bp, 0, 0, report.QT, out int previousLevel);
        _characterManager.PublishLiveProgression(report.Pid, previousLevel, progress);
        _battleManager.ApplyBattleDurability(report);
        if (resultZone != null)
            _battleServerManager.GrantBattleFieldRewards(report, resultZone,
                _battleServerManager.DominatingVictoryOf(client.Id));
        foreach (var update in _questStore.RecordBattleZoneMatch(report.Pid, false, report.GameType,
                     report.Won, report.Kills))
            _logger.LogInformation("[Quests] PID {0} battle result advanced quest {1} objective {2} to {3}.",
                report.Pid, update.QuestIndex, update.ObjectiveIndex, update.Count);
        _logger.LogInformation("[BattleBackend] credited PID {0} +{1} BP (total {2}, level {3}).",
            report.Pid, bp, progress.BP, progress.Level);
        return true;
    }
}
