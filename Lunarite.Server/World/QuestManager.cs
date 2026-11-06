using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class QuestManager
{
    private readonly ILogger _logger;

    private readonly CharacterStore _characterStore;
    private readonly PlayerManager _playerManager;
    private readonly PlayerStore _playerStore;
    private readonly QuestConditions _questConditions;
    private readonly QuestStore _questStore;

    private readonly CityZoneCatalog _cityZoneCatalog;
    private readonly QuestCatalog _questCatalog;

    private readonly QuestRoomMembers _questRoomMembers;

    public QuestManager(ILogger<QuestManager> logger, CharacterStore characterStore,
        PlayerManager playerManager, PlayerStore playerStore, QuestConditions questConditions,
        QuestStore questStore, CityZoneCatalog cityZoneCatalog, QuestCatalog questCatalog, QuestRoomMembers questRoomMembers)
    {
        _questRoomMembers = questRoomMembers;
        _logger = logger;
        _characterStore = characterStore;
        _playerManager = playerManager;
        _playerStore = playerStore;
        _questConditions = questConditions;
        _questStore = questStore;
        _cityZoneCatalog = cityZoneCatalog;
        _questCatalog = questCatalog;
    }

    public bool IsPlayerInQuest(int pid) => pid > 0
        && (_questRoomMembers.IsDetached(pid)
            || (Game.TryGetClient(pid, out var client)
                && _questRoomMembers.QuestOf(client.Id) != 0));

    // Recipient client id -> (sharer PID, quest index) offered through op47.
    // Native SendQuestShareConsent is client op43 `dd` with those same two fields.
    private readonly ConcurrentDictionary<int, (int SharerPid, int QuestIndex)> _pendingQuestShares = [];
    private const byte QuestRefreshAcquire = 1;
    private const byte QuestRefreshAvailability = 2;

    // A quest list sent between LoadMap and the city Spawn runs delegates owned by the outgoing
    // HUD and can strand its QuestListMenu in the new world, so these wait for Spawn.

    public byte MergeQuestTravelRefresh(byte current, bool acquire, bool availability) =>
        (byte)(current | (acquire ? QuestRefreshAcquire : 0)
                      | (availability ? QuestRefreshAvailability : 0));

    internal void QueueQuestTravelRefresh(Client client, bool acquire, bool availability)
    {
        client.Session.QueueQuestRefresh(MergeQuestTravelRefresh(0, acquire, availability));
    }

    internal void FlushQuestTravelRefresh(Client client, int pid)
    {
        // op217 writes its record over the slot holding that item, then clears the addressed slot.
        // Addressing the same slot would wipe the gear; armor cell 5 does not exist, so nothing is
        // cleared, and OnRemoveItem only acts on MAIN. op82 is refused for occupied slots.
        if (client.Session.TryTakeEquipmentRefresh())
            foreach (var item in _playerStore.GetInventory(pid, () => [])
                         .Where(i => i.InventoryIndex is InventoryType.Weapon or InventoryType.Armor).ToList())
                client.SendPacket(new ItemReplacement
                {
                    RemovedInventory = InventoryType.Armor, RemovedCell = 5,
                    Item = ItemManager.ToInsertItem(item)
                });
        if (!client.Session.TryTakeQuestRefresh(out byte refresh)) return;
        if ((refresh & QuestRefreshAcquire) != 0) SendQuestAcquireList(client, pid);
        if ((refresh & QuestRefreshAvailability) != 0) SendQuestAvailabilityUpdate(client, pid);
    }

    public static bool IsQuestRallyTelemetryValid(byte rallyPoint, byte maxRallyPoint, byte complete,
        int squadBp, int bp, int qt, int killedMonsters, int spawnedMonsters, int playTime) =>
        complete <= 3 && rallyPoint <= maxRallyPoint && squadBp >= 0 && qt >= 0
        && killedMonsters >= 0 && spawnedMonsters >= 0 && playTime >= 0;

    public static bool CanEnterQuestByState(bool acquired, string? questType, bool available) =>
        acquired || (available && string.Equals(questType, "QuestType_Free", StringComparison.Ordinal));

    public bool IsCityObjectiveReportAuthorized(int selectedPid, int wirePid,
        int questIndex, bool acquired)
    {
        var quest = _questCatalog.Get(questIndex);
        return selectedPid > 0 && wirePid == selectedPid && acquired
            && quest is { UseQuestRoom: false }
            && quest.QuestType is "QuestType_City" or "QuestType_FreeQuest";
    }

    // HuxleyQuestPlayerController.SendFreeQuestObjectivesInternal reports the run's kills for every
    // acquired QuestType_FreeQuest (FQOType_KillEnemy / Kills_ByWeapon / PickItem) from any field
    // instance. The record names the journal quest, but its PID is the instance MMOPID (room quest
    // index for the host, own PID for a guest).
    public bool IsFreeQuestObjectiveReportAuthorized(int selectedPid, int wirePid,
        int activeQuest, int activeInstance, int questIndex, bool acquired) =>
        selectedPid > 0 && acquired && activeInstance > 0
        && (wirePid == selectedPid || wirePid == activeQuest)
        && _questCatalog.Get(questIndex) is { QuestType: "QuestType_FreeQuest" };

    // NOTACQUIRE for a journal quest not acquired, else IMPOSSIBLE.
    public byte QuestUnavailableAnswer(int pid, int questIndex) =>
        !_questStore.Acquired(pid).Contains(questIndex)
            && !string.Equals(_questCatalog.Get(questIndex)?.QuestType, "QuestType_Free", StringComparison.Ordinal)
            ? QuestResultCode.Notacquire : QuestResultCode.Impossible;

    public bool CanEnterQuest(int pid, int questIndex)
    {
        if (pid <= 0 || questIndex <= 0) return false;
        bool acquired = _questStore.Acquired(pid).Contains(questIndex);
        var quest = _questCatalog.Get(questIndex);
        bool available = quest != null && _questConditions.Available(pid).Contains(questIndex);
        return CanEnterQuestByState(acquired, quest?.QuestType, available);
    }

    // Client-hosted quests identify the local listen-server player with op42's room PID
    // (questIndex), while persistence uses the selected character PID.
    public static bool IsLocalQuestSpawnRequestAuthorized(
        int selectedPid, int questIndex, int instanceToken, int requestedPid) =>
        selectedPid > 0 && questIndex > 0 && instanceToken > 0
        && (requestedPid == questIndex || requestedPid == selectedPid);

    public static bool IsQuestReportIdentityAuthorized(
        int selectedPid, int wirePid, int activeQuest, int instanceToken,
        int? reportedQuest, bool reportedQuestAuthorized) =>
        selectedPid > 0 && activeQuest > 0 && instanceToken > 0
        && (!reportedQuest.HasValue || reportedQuestAuthorized)
        && (wirePid == selectedPid || wirePid == activeQuest);

    public bool IsQuestLevelZone(int zoneIndex) =>
        _cityZoneCatalog.Get(zoneIndex) is { ZoneType: "Zone_TutorialQuest" } zone && int.TryParse(zone.Level, out _);

    public static bool IsQuestAbortLeave(int activeQuest) => activeQuest > 0;

    internal void SendQuestAcquireList(Client client, int pid)
    {
        var questAcquireList = new QuestAcquireList();
        foreach (int index in _questStore.Acquired(pid))
            questAcquireList.Quests.Add(AcquiredQuest(pid, index));

        client.SendPacket(questAcquireList);
    }

    internal void FinishQuestShareAcquire(Client recipientClient, int recipientPid,
        int questIndex, byte answer)
    {
        if (!_pendingQuestShares.TryGetValue(recipientClient.Id, out var pending)
            || pending.QuestIndex != questIndex
            || !((ICollection<KeyValuePair<int, (int SharerPid, int QuestIndex)>>)_pendingQuestShares)
                .Remove(new KeyValuePair<int, (int SharerPid, int QuestIndex)>(recipientClient.Id, pending)))
            return;
        if (Game.TryGetClient(pending.SharerPid, out var sharerClient))
            sharerClient.SendPacket(new QuestShare
                { Pid = recipientPid, QuestIndex = questIndex, Answer = answer });
    }

    private QuestAcquireList.AcquiredQuest AcquiredQuest(int pid, int index)
    {
        var progress = _questStore.GetProgress(pid, index);
        var result = new QuestAcquireList.AcquiredQuest
        {
            Index = index,
            RallyPoint = progress.RallyPoint,
            MaxRallyPoint = progress.MaxRallyPoint,
            Complete = progress.Complete
        };
        foreach (var (objectiveIndex, objective) in progress.Objectives.OrderBy(x => x.Key).Take(8))
        {
            int slot = Math.Clamp(objectiveIndex, 0, 7);
            result.Objectives[slot] = new QuestAcquireList.ObjectiveInfo
            {
                Count = (byte)Math.Clamp(objective.Count, 0, byte.MaxValue),
                Complete = (byte)(objective.Count >= (_questCatalog.Get(index)?.Objectives
                    .FirstOrDefault(x => x.Index == objectiveIndex)?.CompleteCount ?? int.MaxValue) ? 1 : 0),
                Visible = objective.Visible ? (byte)1 : (byte)0
            };
        }
        return result;
    }

    public static QuestShare BuildQuestShareDisconnectResult(int recipientPid,
        int sharerPid, int questIndex) => new()
    {
        Pid = recipientPid,
        QuestIndex = questIndex,
        Answer = QuestAcquire.NotFoundPlayer
    };

    internal static void SendQuestInventoryChanges(Client client, IEnumerable<byte> changedCells,
        IEnumerable<PlayerStore.GenericItem> inserted)
    {
        foreach (byte cell in changedCells.Distinct())
            client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = cell });
        foreach (var item in inserted) ItemManager.SendGrantedItem(client, item);
    }

    // Destination order: an explicit client request, the quest's cooked ExitZoneIndex, the city the
    // player departed from (the saved city location is not changed by quest travel), then the
    // faction Training Center. A cooked ExitAreaIndex with no zone is an area of the departure city:
    // Q-890022 has area 13, Eska's ESKA_RS_02, and the Training Center has only 6 areas.
    public (int ZoneIndex, int AreaIndex) ResolveQuestExit(
        int requestedZone, int requestedArea, int factionCamp,
        int questExitZone = -1, int questExitArea = -1, (int Zone, int Area)? departure = null)
    {
        if (requestedZone >= 0 && _cityZoneCatalog.IsKnown(requestedZone)) return (requestedZone, requestedArea);
        if (questExitZone >= 0 && _cityZoneCatalog.IsKnown(questExitZone))
            return (questExitZone, questExitArea >= 0 ? questExitArea : 0);
        if (departure is { } d && d.Zone >= 0 && _cityZoneCatalog.IsKnown(d.Zone) && !IsQuestLevelZone(d.Zone))
            return (d.Zone, questExitArea >= 0 ? questExitArea : Math.Max(0, d.Area));
        return (factionCamp, questExitArea >= 0 ? questExitArea : 0);
    }

    internal int QuestParticipantClientId(int pid) =>
        Game.TryGetClient(pid, out var liveClient)
            ? liveClient.Id : _questRoomMembers.DetachedClientOf(pid);

    internal static Client? QuestRewardLiveRecipient(int pid) =>
        Game.TryGetClient(pid, out var liveClient)
            && Game.PidOf(liveClient) == pid ? liveClient : null;

    public static bool ShouldReturnQuestMemberOnHostExit(int departingClientId,
        int departingPid, int departingInstance, bool departingWasHost,
        int candidateClientId, int candidateInstance) =>
        departingWasHost && departingPid > 0 && departingInstance > 0
        && candidateClientId != departingClientId && candidateInstance == departingInstance;

    public static bool IsExpectedQuestTravelDisconnect(int pid, int instanceToken,
        bool instanceHasHost, bool travelWasArmed) => pid > 0 && instanceToken > 0
        && instanceHasHost && travelWasArmed;

    // Quest identity drives progression; the separate host-PID token isolates concurrent runs.

    // Level, BP, LP, money and QT as op16 parameters (values above int.MaxValue are clamped).
    internal static void SendProgressParameters(Client client, PlayerStore.PlayerProgress progress)
    {
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_LEVEL, Value = progress.Level });
        client.SendPacket(new Parameter
        {
            Param = Parameter.PARAMETER_BP, Value = (int)Math.Min(int.MaxValue, progress.BP)
        });
        client.SendPacket(new Parameter
        {
            Param = Parameter.PARAMETER_LP, Value = (int)Math.Min(int.MaxValue, progress.LP)
        });
        client.SendPacket(new Parameter
        {
            Param = Parameter.PARAMETER_MONEY, Value = (int)Math.Min(int.MaxValue, progress.Money)
        });
        client.SendPacket(new Parameter
        {
            Param = Parameter.PARAMETER_QT, Value = (int)Math.Min(int.MaxValue, progress.QT)
        });
    }

    internal void SendQuestAvailabilityUpdate(Client client, int pid)
    {
        if (pid <= 0) return;
        client.SendPacket(new ServerSideQuestPossibleList { Quests = _questConditions.Available(pid) });
    }

    public static bool AreQuestPartyMembersOnline(IEnumerable<int> members, IEnumerable<int> onlinePids)
    {
        var online = onlinePids.ToHashSet();
        var party = members.ToList();
        return party.Count > 0 && party.All(pid => pid > 0 && online.Contains(pid));
    }

    public static bool AreQuestPartyMembersEligible(IEnumerable<int> members, Func<int, bool> canEnter)
    {
        var party = members.Distinct().ToList();
        return party.Count > 0 && party.All(pid => pid > 0 && canEnter(pid));
    }

    internal bool TryTakePendingShare(int clientId, out (int SharerPid, int QuestIndex) share) =>
        _pendingQuestShares.TryRemove(clientId, out share);

    internal void SetPendingShare(int clientId, (int SharerPid, int QuestIndex) share) => _pendingQuestShares[clientId] = share;

    internal bool HasPendingShare(int clientId) => _pendingQuestShares.ContainsKey(clientId);
}
