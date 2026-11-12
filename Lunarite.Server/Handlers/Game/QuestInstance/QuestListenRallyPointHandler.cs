using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// US-client native wire order: PID, Quest, Rally, MaxRally, Complete, SquadBP, BP, QT,
// KilledMonsters, SpawnedMonsters, PlayTime (op54, format "ddbbbdddddd").
[PacketHandler]
public static class QuestListenRallyPointHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static QuestCatalog _questCatalog = null!;
    private static CharacterManager _characterManager = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestStore _questStore = null!;
    private static QuestRewards _questRewards = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestListenRallyPointHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
        _questRewards = serviceProvider.GetRequiredService<QuestRewards>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestListenRallyPointRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("QuestListenRallyPoint: malformed payload {0}.", Convert.ToHexString(reader.Span));
            return false;
        }

        int selectedPid = Game.PidOf(client);
        int ownerPid = _questInstanceManager.ResolveQuestReportOwner(client, packet.Pid, packet.QuestIndex);
        bool telemetryValid = QuestManager.IsQuestRallyTelemetryValid(packet.RallyPoint, packet.MaxRallyPoint,
            packet.Complete, packet.SquadBP, packet.BP, packet.QT, packet.KilledMonsters,
            packet.SpawnedMonsters, packet.PlayTime);
        bool bpValid = ownerPid > 0 && _playerStore.CanApplyMissionBp(ownerPid, packet.BP);
        int instanceToken = _questRoomMembers.InstanceOf(client.Id);
        // A helper's run is settled like a field mission's: its kill BP, no quest progress.
        bool helper = ownerPid > 0 && packet.QuestIndex == _questRoomMembers.QuestOf(client.Id)
            && _questInstanceManager.IsQuestHelper(ownerPid, packet.QuestIndex, instanceToken);
        bool directField = ownerPid > 0
            && packet.QuestIndex == _questRoomMembers.QuestOf(client.Id)
            && (helper || string.Equals(_questCatalog.Get(packet.QuestIndex)?.QuestType, "QuestType_Free", StringComparison.Ordinal))
            && !_questStore.Acquired(ownerPid).Contains(packet.QuestIndex);
        bool canApply = directField ? telemetryValid && bpValid : ownerPid > 0 && bpValid;
        // A guest's report arrives on the host's link: the host may not take more of the guest's
        // BP than its death penalties allow (QuestInstanceManager.ClampRunReward).
        bool otherParticipant = ownerPid != selectedPid;
        var (valid, settledProgress, previousLevel, credited) = canApply
            ? ApplyRally(ownerPid, instanceToken, packet, directField, telemetryValid, bpValid, otherParticipant, helper)
            : (false, null, 0, (0, 0));
        if (valid) _questInstanceManager.CommitQuestEntryOnReward(instanceToken, ownerPid);
        if (settledProgress != null)
        {
            // Guest telemetry arrives on the host's link; the guest's own balance and level
            // updates go only to the guest (or wait for its reconnect).
            _characterManager.PublishLiveProgression(ownerPid, previousLevel, settledProgress);
            _logger.LogInformation(directField
                    ? "[Quests] settled direct field quest {0} for PID {1}: {2:+0;-0} BP, +{3} QT."
                    : "[Quests] settled quest {0} for PID {1}: {2:+0;-0} BP, +{3} QT.",
                packet.QuestIndex, ownerPid, credited.BP, credited.QT);
            if (credited != (packet.BP, packet.QT))
                _logger.LogWarning("[Quests] PID {0} quest {1} settlement {2} BP / {3} QT exceeded the run allowance.",
                    ownerPid, packet.QuestIndex, packet.BP, packet.QT);
        }
        if (!valid)
        {
            _logger.LogWarning("QuestListenRallyPoint rejected: PID={0} selected={1} quest={2} rally={3}/{4} complete={5}.",
                packet.Pid, selectedPid, packet.QuestIndex, packet.RallyPoint, packet.MaxRallyPoint, packet.Complete);
        }
        else _logger.LogInformation(
            "[Quests] PID {0} quest {1} rally {2}/{3}, complete={4}, BP={5}, QT={6}, squadBP={7}, kills={8}, spawns={9}, playTime={10}.",
            ownerPid, packet.QuestIndex, packet.RallyPoint, packet.MaxRallyPoint, packet.Complete,
            packet.BP, packet.QT, packet.SquadBP, packet.KilledMonsters, packet.SpawnedMonsters, packet.PlayTime);
        client.SendPacket(new QuestProgressAnswer { Opcode = 56, Answer = valid ? (byte)0 : (byte)1,
            QuestIndex = packet.QuestIndex, Index = packet.RallyPoint, Count = packet.MaxRallyPoint,
            State = packet.Complete });
        return true;
    }

    // Records the rally report and, for a settlement report, credits its BP and QT (clamped to the
    // run allowance, QuestInstanceManager.RunLimits), in one transaction. A rally report's running
    // total is stored clamped to what the run could still credit, with the run it belongs to,
    // since quest completion and a host loss pay (and book) it later. A direct-field run is
    // recorded without a journal acquisition.
    private static (bool Valid, PlayerStore.PlayerProgress? Settled, int PreviousLevel, (int BP, int QT) Credited)
        ApplyRally(int ownerPid, int instanceToken, QuestListenRallyPointRequest packet, bool directField,
            bool telemetryValid, bool bpValid, bool otherParticipant, bool helper = false)
    {
        long questRewardBp = _questRewards.Get(ownerPid, packet.QuestIndex).BP;
        var (storedBp, storedQt) = QuestStore.IsSettlementReport(packet.RallyPoint, packet.MaxRallyPoint, packet.Complete)
            ? (packet.BP, packet.QT)
            : _questInstanceManager.ClampRunReward(instanceToken, ownerPid, packet.BP, Math.Max(0, packet.QT),
                otherParticipant, questRewardBp, book: false);
        if (packet.QT < 0) storedQt = packet.QT; // still rejected by the store's validation
        long runStarted = _questInstanceManager.StartedTicks(instanceToken);
        bool valid = false;
        PlayerStore.PlayerProgress? settled = null;
        int previousLevel = 0;
        (int BP, int QT) credited = (0, 0);
        _multiStoreTransaction.Run(() =>
        {
            valid = directField
                ? _questStore.UpdateDirectFieldRally(ownerPid, packet.QuestIndex, packet.RallyPoint,
                    packet.MaxRallyPoint, packet.Complete, packet.SquadBP, storedBp, storedQt,
                    packet.KilledMonsters, packet.SpawnedMonsters, packet.PlayTime, instanceToken, runStarted, helper)
                : _questStore.UpdateRally(ownerPid, packet.QuestIndex, packet.RallyPoint,
                    packet.MaxRallyPoint, packet.Complete, packet.SquadBP, storedBp, storedQt,
                    packet.KilledMonsters, packet.SpawnedMonsters, packet.PlayTime, instanceToken, runStarted);
            if (valid && QuestInstanceManager.IsDirectFieldSettlement(telemetryValid, bpValid, packet.RallyPoint,
                    packet.MaxRallyPoint, packet.Complete, packet.BP, packet.QT))
            {
                credited = _questInstanceManager.ClampRunReward(instanceToken, ownerPid, packet.BP, packet.QT,
                    otherParticipant, questRewardBp);
                settled = _playerStore.AddReward(ownerPid, 0, credited.BP, 0, 0, credited.QT, out previousLevel);
            }
        });
        return (valid, settled, previousLevel, credited);
    }
}
