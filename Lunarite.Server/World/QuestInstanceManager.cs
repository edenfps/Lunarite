using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed partial class QuestInstanceManager
{
    private readonly ILogger _logger;
    private readonly BattleManager _battleManager;
    private readonly CityManager _cityManager;
    private readonly ItemManager _itemManager;
    private readonly P2PManager _p2pManager;
    private readonly QuestManager _questManager;

    private readonly CharacterStore _characterStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly LicenseStore _licenseStore;
    private readonly PersonalBuffStore _personalBuffStore;
    private readonly PlayerManager _playerManager;
    private readonly PlayerStore _playerStore;
    private readonly QuestEntryReservationStore _questEntryReservationStore;
    private readonly QuestStore _questStore;

    private readonly QuestCatalog _questCatalog;
    private readonly ServerEvents _serverEvents;

    private readonly QuestGateway _questGateway;

    private readonly MultiStoreTransaction _multiStoreTransaction;

    private readonly ConcurrentDictionary<int, PendingQuestDecision> _pendingQuestDecisions = [];

    private readonly ConcurrentDictionary<int, QuestTransport> _questTransports = [];

    private readonly ConcurrentDictionary<int, QuestGuestArrivals> _questGuestArrivals = [];
    private readonly ConcurrentDictionary<int, int> _questInstanceHosts = []; // instance token -> host PID
    // A city link closed for armed quest travel keeps its old client-id binding until the
    // character is selected again in the city.
    // Listen-world controller backups are stored but never grant items or overwrite BP; the
    // retail restore transaction is unknown.
    private readonly ConcurrentDictionary<(int Instance, int Pid), QuestGameDataRequest> _questGameBackups = [];

    private readonly QuestRoomMembers _questRoomMembers;

    private readonly CityZoneCatalog _cityZoneCatalog;

    private readonly SquadManager _squadManager;

    public QuestInstanceManager(ILogger<QuestInstanceManager> logger, CharacterStore characterStore,
        DurativeItemStore durativeItemStore, LicenseStore licenseStore, PersonalBuffStore personalBuffStore,
        PlayerManager playerManager, PlayerStore playerStore,
        QuestEntryReservationStore questEntryReservationStore, QuestStore questStore,
        BattleManager battleManager, CityManager cityManager, ItemManager itemManager, P2PManager p2pManager,
        QuestManager questManager, QuestCatalog questCatalog, ServerEvents serverEvents, QuestGateway questGateway, MultiStoreTransaction multiStoreTransaction, QuestRoomMembers questRoomMembers, CityZoneCatalog cityZoneCatalog, SquadManager squadManager)
    {
        _squadManager = squadManager;
        _cityZoneCatalog = cityZoneCatalog;
        _questRoomMembers = questRoomMembers;
        _logger = logger;
        _characterStore = characterStore;
        _durativeItemStore = durativeItemStore;
        _licenseStore = licenseStore;
        _personalBuffStore = personalBuffStore;
        _playerManager = playerManager;
        _playerStore = playerStore;
        _questEntryReservationStore = questEntryReservationStore;
        _questStore = questStore;
        _battleManager = battleManager;
        _cityManager = cityManager;
        _itemManager = itemManager;
        _p2pManager = p2pManager;
        _questManager = questManager;
        _questCatalog = questCatalog;
        _serverEvents = serverEvents;
        _questGateway = questGateway;
        _multiStoreTransaction = multiStoreTransaction;
    }

    public static bool IsQuestDifficultyValid(byte difficulty) => difficulty <= 2;

    public enum QuestTransportSignal : byte
    {
        RoomDataInstalled,
        HostRoomLive,
        GatewayServerReady
    }

    private void RejectQuestInstance(Client hostClient, int hostPid,
        PendingQuestDecision? acceptedGroup, byte answer)
    {
        hostClient.SendPacket(new QuestCreate { Answer = answer });
        if (acceptedGroup == null) return;
        _acceptedQuestGroups.TryRemove(hostPid, out _);
        // OnQuestCreate(error) unlocks only the host. Guests may still have bTryCreateQuest/input
        // locked; SQUAD_NOT_ALL_ENTER clears it.
        foreach (int guestPid in QuestCreateFailureGuestPids(hostPid, acceptedGroup.Members))
            if (Game.TryGetClient(guestPid, out var guestClient))
                guestClient.SendPacket(ChatManager.BuildQuestDecisionFailureNotice(acceptedGroup.LeaderPid));
    }

    // rehost: the room replaces one whose host was lost, so the entry items are not taken again.
    internal bool QuestCreate(Client client, int questRequestIndex, byte difficultyLevel, bool rehost = false)
    {
        int selectedPid = Game.PidOf(client);
        _acceptedQuestGroups.TryGetValue(selectedPid, out var acceptedEntryGroup);
        if (acceptedEntryGroup?.QuestIndex != questRequestIndex) acceptedEntryGroup = null;
        var entryPids = acceptedEntryGroup != null
            ? acceptedEntryGroup.Members.Distinct().ToList() : [selectedPid];

        byte rejection = ValidateQuestRoomRequest(selectedPid, questRequestIndex, difficultyLevel, entryPids,
            out var quest);
        if (rejection == 0 && !TryConsumeQuestEntryItems(selectedPid, quest!, entryPids, rehost))
            rejection = QuestResultCode.DonthaveEnterItem;
        if (rejection != 0)
        {
            RejectQuestInstance(client, selectedPid, acceptedEntryGroup, rejection);
            return true;
        }

        // The host PID is the instance token; QuestIndex is not unique because separate parties
        // can run the same quest at once.
        var questIndex = quest!.EntryIndex;
        QuestLootGrants.ClearInstance(selectedPid);
        _questInstanceHosts[selectedPid] = selectedPid;
        _questInstanceQuests[selectedPid] = questIndex;
        _questInstanceStarted[selectedPid] = DateTime.UtcNow.Ticks;
        _cityManager.LeaveCurrentPresence(client, selectedPid);
        _questRoomMembers.SetQuest(client.Id, questIndex);
        _questRoomMembers.SetInstance(client.Id, selectedPid);
        _questRoomMembers.SetDifficulty(client.Id, difficultyLevel);

        if (Environment.GetEnvironmentVariable("DEDICATED_QUEST") == "1")
        {
            SendDedicatedQuestJoin(client, selectedPid);
            return true;
        }

        LaunchQuestHost(client, selectedPid, quest, difficultyLevel);
        return true;
    }

    // Sends the host its op42 world launch and op55 member permissions, prepares the squad
    // transport and starts the quest gateway.
    private void LaunchQuestHost(Client client, int selectedPid, QuestCatalog.Quest quest, byte difficultyLevel)
    {
        int questIndex = quest.EntryIndex;
        // ConnData[16] is the room verify KEY: the host stores it (sub_1119CFD0 -> 0x123EEA48) and
        // its MMOPermission table checks a joining gateway's HELLO KEY against it (sub_11197060).
        // It is a one-time room credential, so every instance gets a new random 128-bit key.
        var connData = NewQuestConnectionKey();
        var questCreate = BuildQuestCreate(selectedPid, quest, difficultyLevel, connData);
        _logger.LogDebug("QuestCreate host map for {0} = '{1}' (questIndex={2})", questIndex, quest.Level, questIndex);

        // An op127 NAT test may report the actual P2P socket ports through op155; otherwise 7777.
        var hostNatEndpoints = client.Session.NatEndpoint;
        var hostRelayAddresses = client.Session.NetworkAddresses;
        var hostReportedPublic = P2PManager.ReportedPublicEndpoint(hostNatEndpoints, hostRelayAddresses);
        ushort hostPublicPort = hostReportedPublic?.Port ?? 7777;
        ushort hostPeerPort = hostNatEndpoints?.LocalPort ?? 7777;

        // The squad transport must exist before op42 makes the host load its listen world, so a
        // room-live notice can find it.
        var memberPermissions = new List<QuestPermissionRegister>();
        if (_acceptedQuestGroups.TryRemove(selectedPid, out var acceptedGroup)
            && acceptedGroup.QuestIndex == questIndex)
        {
            memberPermissions.AddRange(acceptedGroup.Members.Where(x => x != selectedPid)
                .Select(memberPid => BuildQuestMemberPermission(memberPid, connData)));
            PrepareQuestTransport(client, selectedPid, questIndex, difficultyLevel, acceptedGroup.Members,
                memberPermissions, connData, hostNatEndpoints, hostReportedPublic, hostPublicPort, hostPeerPort);
        }

        // The P2P listen host keeps this city link through loading and the mission (op199 and the
        // op56/op59 loot traffic ride it), so it is not armed for quest travel: a close before
        // op199 is a crash or quit and abandons the room. op55 must follow op42: it registers
        // each guest's PID + KEY in the host's MMOPermission table.
        _logger.LogInformation("[QuestTransport] sending host PID {0} op42 and op55 permission PIDs [{1}] for quest {2}.",
            selectedPid, string.Join(",", new[] { questIndex }.Concat(memberPermissions.Select(x => x.Pid))), questIndex);
        foreach (var launchPacket in BuildQuestHostLaunch(questCreate, connData, memberPermissions))
            client.SendPacket(launchPacket);
        if (memberPermissions.Count > 0 && _questTransports.TryGetValue(selectedPid, out var pendingTransport))
            _ = RefreshQuestMemberPermissionsUntilReady(selectedPid, pendingTransport, TimeSpan.FromSeconds(10));

        // The gateway's MMO HELLO uses the room PID (op42 field #3 = questIndex) and ConnData KEY.
        // An op42-hosted quest is playable without it, so it is off by default (QuestGateway.Enabled).
        _questGateway.ConfigureRoom(questIndex, connData, client, ownerPid: selectedPid,
            hostPort: hostPublicPort,
            roomReady: OnQuestGatewayServerReady);
        if (!QuestGateway.Enabled)
            _logger.LogWarning("[Gateway] off for quest host PID {0} (QUEST_GATEWAY_DISABLED=0 turns it on).", selectedPid);
        else
            _questGateway.StartHandshake(selectedPid);

        // DEBUG_LOOT=1 sends op59 on a timer to tell whether the client's kill path arms
        // Delegate_DropItem (a pickup appears) when op56 kill reports do not arrive.
        if (Environment.GetEnvironmentVariable("DEBUG_LOOT") == "1")
            StartLootProbe(client);

    }

    // Re-checks the party at creation: the host may not be the leader on a same-PC party, and a
    // member can leave between consent and creation. 0 = accepted.
    private byte ValidateQuestRoomRequest(int selectedPid, int questRequestIndex, byte difficultyLevel,
        List<int> entryPids, out QuestCatalog.Quest? quest)
    {
        quest = null;
        if (!IsQuestDifficultyValid(difficultyLevel))
        {
            _logger.LogWarning("[Quest] PID {0} attempted quest {1} with invalid difficulty {2}.",
                selectedPid, questRequestIndex, difficultyLevel);
            return QuestResultCode.Error;
        }
        if (!QuestManager.AreQuestPartyMembersEligible(entryPids, pid => _questManager.CanEnterQuest(pid, questRequestIndex)))
        {
            _logger.LogWarning("[Quest] party [{0}] attempted to create unavailable quest {1} through host PID {2}.",
                string.Join(",", entryPids), questRequestIndex, selectedPid);
            return QuestResultCode.Error;
        }
        if (_questEntryReservations.ContainsKey(selectedPid))
        {
            _logger.LogWarning("[Quest] PID {0} attempted another room while entry items are reserved.", selectedPid);
            return QuestResultCode.FailLoadingroom;
        }
        quest = _questCatalog.Get(questRequestIndex);
        if (quest == null)
        {
            _logger.LogWarning(
                "[Quest] rejecting unknown quest EntryIndex {0}; it is absent from HuxleyQuestData.",
                questRequestIndex);
            return QuestResultCode.Error;
        }
        if (string.IsNullOrWhiteSpace(quest.Level))
        {
            _logger.LogWarning("[Quest] quest {0} ({1}, {2}) has no Level and is not an instance quest.",
                quest.EntryIndex, quest.QuestName, quest.QuestType);
            return QuestResultCode.Error;
        }
        if (!_questCatalog.ClientHasMap(quest.Level))
        {
            _logger.LogError("[Quest] quest {0} requires missing client map package '{1}'.", questRequestIndex, quest.Level);
            return QuestResultCode.Error;
        }
        // Checked before any entry items are consumed.
        if (!QuestManager.AreQuestPartyMembersOnline(entryPids, _playerManager.OnlinePids))
        {
            _logger.LogWarning("[Quest] party member disconnected before room creation for quest {0}: [{1}].",
                questRequestIndex, string.Join(",", entryPids));
            return QuestResultCode.NotfoundPlayer;
        }
        byte partySizeAnswer = ValidateQuestPartySize(quest, entryPids.Count);
        if (partySizeAnswer != 0)
        {
            _logger.LogWarning("[Quest] party size {0} rejected for quest {1} (allowed {2}..{3}).",
                entryPids.Count, questRequestIndex, quest.MinPlayers, quest.MaxPlayers);
            return partySizeAnswer;
        }
        foreach (int entryPid in entryPids)
        {
            byte equipmentAnswer = ValidateQuestEquipment(_playerStore.GetInventory(entryPid, () => []));
            if (equipmentAnswer == 0) continue;
            _logger.LogWarning("[Quest] PID {0} equipment rejected for quest {1} with result {2}.",
                entryPid, questRequestIndex, equipmentAnswer);
            return equipmentAnswer;
        }
        return 0;
    }

    // Takes every member's EnterRequestItems in one transaction and persists the reservation so a
    // crash refunds them.
    private bool TryConsumeQuestEntryItems(int selectedPid, QuestCatalog.Quest quest, List<int> entryPids,
        bool rehost = false)
    {
        bool consumed = false;
        Dictionary<int, List<byte>> consumedByPid = [];
        IReadOnlyList<QuestCatalog.ItemRequirement> entryItems = rehost ? [] : quest.EnterRequestItems;
        _multiStoreTransaction.Run(() =>
        {
            consumed = _playerStore.TryConsumeQuestEntryItems(entryPids,
                entryItems.Select(x => (x.ItemIndex, x.ItemCount)), out consumedByPid);
            if (!consumed) return;
            if (entryItems.Count != 0)
                _questEntryReservationStore.Put(selectedPid, entryPids,
                    quest.EnterRequestItems.Select(x => (x.ItemIndex, x.ItemCount)));
            foreach (int entryPid in entryPids)
            {
                _questStore.BeginDirectFieldRun(entryPid, quest.EntryIndex);
                _itemManager.SynchronizeQuestPickupObjectives(entryPid,
                    quest.EnterRequestItems.Select(item => item.ItemIndex));
            }
        });
        if (!consumed)
        {
            _logger.LogWarning("[Quest] party [{0}] lacks required entry items for quest {1}: {2}.",
                string.Join(",", entryPids), quest.EntryIndex,
                string.Join(", ", quest.EnterRequestItems.Select(x => $"{x.ItemIndex}x{x.ItemCount}")));
            return false;
        }
        foreach (var (entryPid, consumedCells) in consumedByPid)
        {
            if (!Game.TryGetClient(entryPid, out var entryClient)) continue;
            foreach (byte cell in consumedCells.Distinct())
            {
                entryClient.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = cell });
                var remaining = _playerStore.GetInventory(entryPid, () => [])
                    .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == cell);
                if (remaining != null) ItemManager.SendInventoryItem(entryClient, remaining);
            }
        }
        if (quest.EnterRequestItems.Count != 0)
        {
            _questEntryReservations[selectedPid] = new QuestInstanceManager.QuestEntryReservation
            {
                Members = entryPids.Distinct().ToList(),
                Items = quest.EnterRequestItems.Select(x => (x.ItemIndex, x.ItemCount)).ToList()
            };
        }
        return true;
    }

    internal void CleanupDetachedQuestSession(int pid)
    {
        if (!_questRoomMembers.TryTakeDetached(pid, out int oldClientId)) return;
        _questRoomMembers.ClearQuest(oldClientId);
        _questRoomMembers.ClearDifficulty(oldClientId);
        if (!_questRoomMembers.TryClearInstance(oldClientId, out int instanceToken)) return;
        _questGameBackups.TryRemove((instanceToken, pid), out _);
        QuestLootGrants.ClearParticipant(instanceToken, pid);
        if (!_questInstanceHosts.TryGetValue(instanceToken, out int hostPid) || hostPid != pid) return;

        _questInstanceHosts.TryRemove(instanceToken, out _);
        foreach (var key in _questGameBackups.Keys.Where(key => key.Instance == instanceToken))
            _questGameBackups.TryRemove(key, out _);
        _questGateway.CancelHandshake(pid);
        _questTransports.TryRemove(pid, out var transport);
        _questGuestArrivals.TryRemove(pid, out _);
        _questEntryReservations.TryRemove(pid, out var reservation);
        if (transport != null)
            foreach (int memberPid in transport.Members.Where(x => x != pid))
                if (Game.TryGetClient(memberPid, out var memberClient))
                    foreach (var packet in BuildFailedQuestTransportGuestPackets(pid))
                        memberClient.SendPacket(packet);
        if (reservation != null)
            _multiStoreTransaction.Run(() =>
            {
                _questEntryReservationStore.Remove(pid);
                foreach (int memberPid in reservation.Members)
                {
                    if (!_playerStore.TryExchangeQuestItems(memberPid, [], reservation.Items,
                            out var restored, out var changedCells))
                    {
                        _logger.LogError(
                            "[Quest] failed to restore reserved entry items for PID {0} after city return.", memberPid);
                        continue;
                    }
                    _itemManager.SynchronizeQuestPickupObjectives(memberPid,
                        reservation.Items.Select(item => item.ItemId));
                    if (Game.TryGetClient(memberPid, out var memberClient))
                        QuestManager.SendQuestInventoryChanges(memberClient, changedCells, restored);
                }
            });
        _logger.LogInformation("[Quest] cleared detached quest session for returning host PID {0}.", pid);
    }

    internal void LeaveQuestInstance(int clientId, int pid)
    {
        _questRoomMembers.ClearDifficulty(clientId);
        if (!_questRoomMembers.TryClearInstance(clientId, out int instanceToken)) return;
        _questGameBackups.TryRemove((instanceToken, pid), out _);
        OpenLootClaimWindows(pid, instanceToken);
        if (pid > 0 && _questInstanceHosts.TryGetValue(instanceToken, out int hostPid) && hostPid == pid)
        {
            _questInstanceHosts.TryRemove(instanceToken, out _);
            foreach (var key in _questGameBackups.Keys.Where(key => key.Instance == instanceToken))
                _questGameBackups.TryRemove(key, out _);
            _questGateway.CancelHandshake(pid);
        }
    }

    // Host PID -> when its current instance was created; tells one run's op60 reports from the next.
    private readonly ConcurrentDictionary<int, long> _questInstanceStarted = new();

    // Host PID of an instance token (the host's own PID while the room is open).
    internal int HostOf(int instanceToken) => _questInstanceHosts.GetValueOrDefault(instanceToken);

    internal bool TryGetHost(int instanceToken, out int hostPid) => _questInstanceHosts.TryGetValue(instanceToken, out hostPid);

    internal bool IsInstanceOpen(int instanceToken) => _questInstanceHosts.ContainsKey(instanceToken);

    internal long StartedTicks(int hostPid) => _questInstanceStarted.GetValueOrDefault(hostPid);

    internal void SaveGameBackup(int instanceToken, int ownerPid, QuestGameDataRequest backup) =>
        _questGameBackups[(instanceToken, ownerPid)] = backup;

    internal bool TryTakeTransport(int hostPid, out QuestTransport? transport) => _questTransports.TryRemove(hostPid, out transport);

    internal bool HasTransport(int hostPid) => _questTransports.ContainsKey(hostPid);

    internal bool TryGetGuestArrivals(int hostPid, out QuestGuestArrivals arrivals) =>
        _questGuestArrivals.TryGetValue(hostPid, out arrivals!);

    internal void ForgetGuestArrivals(int hostPid) => _questGuestArrivals.TryRemove(hostPid, out _);

    internal bool TryGetPendingDecision(int clientId, out PendingQuestDecision pending) =>
        _pendingQuestDecisions.TryGetValue(clientId, out pending!);

    internal void ForgetPendingDecision(int clientId) => _pendingQuestDecisions.TryRemove(clientId, out _);

    // A new login means the client is back at the login screen, so any quest session kept from
    // a travel disconnect is stale (the client crashed while loading or hosting). Otherwise the
    // account would stay locked out, since only selecting the character again releases it.
    public int ReleaseStaleQuestSessions(int userId)
    {
        if (userId <= 0) return 0;
        int released = 0;
        foreach (int pid in _questRoomMembers.DetachedPids
                     .Where(pid => _characterStore.Get(pid)?.OwnerUserId == userId).ToList())
        {
            CleanupDetachedQuestSession(pid);
            _logger.LogInformation("[Quest] released stale quest session of PID {0} on a new login of user {1}.", pid, userId);
            released++;
        }
        return released;
    }

    internal void QuestLeave(Client client, int requestedZone, int requestedArea, string action)
    {
        int pid = Game.PidOf(client);
        if (pid <= 0) return;
        int departingInstance = _questRoomMembers.InstanceOf(client.Id);
        bool departingWasHost = departingInstance > 0
            && TryGetHost(departingInstance, out int instanceHostPid)
            && instanceHostPid == pid;
        int factionCamp = CharacterStore.TrainingCenterMap(_characterStore.Get(pid)?.Index ?? 1);
        var quest = _questCatalog.Get(_questRoomMembers.QuestOf(client.Id));
        var destination = _questManager.ResolveQuestExit(requestedZone, requestedArea, factionCamp,
            quest?.ExitZoneIndex ?? -1, quest?.ExitAreaIndex ?? -1, _characterStore.GetCityLocation(pid));
        byte characterIndex = _characterStore.Get(pid)?.Index ?? 1;
        if (!_cityZoneCatalog.IsValidArea(destination.ZoneIndex, destination.AreaIndex, characterIndex))
            destination = (factionCamp, 0);
        _cityManager.LeaveCurrentPresence(client, pid);
        _questRoomMembers.ClearQuest(client.Id);
        LeaveQuestInstance(client.Id, pid);
        Game.PlayerFor(pid).CityMap = destination.ZoneIndex;
        _characterStore.SetCityLocation(pid, destination.ZoneIndex, destination.AreaIndex);
        // No op11 PlayerInfo here: it answers the PlayerSelect transaction, and
        // HuxleySubsystem.OnPlayerInfo clears the quest lists and calls
        // ResetTransaction_PlayerSelect. During QuestLeave that transaction is NONE, so op11
        // asserts and leaves a quest-list UIScene whose rows no longer respond.
        CityManager.ArmExpectedCityTravel(client);
        client.SendPacket(new LoadMap { Index = destination.ZoneIndex, Position = destination.AreaIndex });
        // The city journal still holds its pre-mission counts; op38 refreshes it after Spawn.
        _questManager.QueueQuestTravelRefresh(client, acquire: true, availability: false);
        _logger.LogInformation("[Quest] PID {0} {1} instance; returning to city zone {2}, area {3}.",
            pid, action, destination.ZoneIndex, destination.AreaIndex);
        if (departingWasHost)
            ReturnQuestMembersAfterHostExit(client.Id, pid, departingInstance, departingWasHost);
    }

    internal void ReturnQuestMembersAfterHostExit(int departingClientId, int departingPid,
        int departingInstance, bool departingWasHost)
    {
        foreach (var member in _questRoomMembers.InstanceMembers())
        {
            if (!QuestManager.ShouldReturnQuestMemberOnHostExit(departingClientId, departingPid, departingInstance,
                    departingWasHost, member.Key, member.Value)) continue;
            var memberClient = _playerManager.OnlineClients.FirstOrDefault(x => x.Id == member.Key);
            if (memberClient != null)
                QuestLeave(memberClient, -1, -1, "lost quest host and left");
        }
    }

    private void OnQuestGatewayServerReady(int hostPid, int roomPid)
    {
        if (!ConfirmQuestHostReady(hostPid, roomPid, QuestInstanceManager.QuestTransportSignal.GatewayServerReady))
            _logger.LogWarning("[Quest] ignored gateway SERVERREADY for host PID {0}, room {1}.",
                hostPid, roomPid);
    }

    internal bool TryGetGameBackup(int instanceToken, int pid, out QuestGameDataRequest backup) =>
        _questGameBackups.TryGetValue((instanceToken, pid), out backup!);

    internal void SetHost(int instanceToken, int hostPid) => _questInstanceHosts[instanceToken] = hostPid;

    internal void ForgetHost(int instanceToken) => _questInstanceHosts.TryRemove(instanceToken, out _);
}
