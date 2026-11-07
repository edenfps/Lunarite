using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// A Game link closed. Releases what the link and its selected character held, keeping the quest
// room of a character that dropped the city link for armed quest or city travel. P2P quest
// hosts and guests keep their city link, so their close is a crash or quit.
public static class DisconnectHandler
{
    private static ILogger _logger = null!;
    private static CharacterManager _characterManager = null!;
    private static QuestStore _questStore = null!;
    private static QuestRewards _questRewards = null!;
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestEntryReservationStore _questEntryReservationStore = null!;
    private static BattleManager _battleManager = null!;
    private static CityManager _cityManager = null!;
    private static ItemManager _itemManager = null!;
    private static PlayerManager _playerManager = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static QuestManager _questManager = null!;
    private static QuestRoomMembers _questRoomMembers = null!;
    private static SimulationManager _simulationManager = null!;
    private static SquadManager _squadManager = null!;
    private static TradeManager _tradeManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {

        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();

        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questRewards = serviceProvider.GetRequiredService<QuestRewards>();
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DisconnectHandler));
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questEntryReservationStore = serviceProvider.GetRequiredService<QuestEntryReservationStore>();
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
        _tradeManager = serviceProvider.GetRequiredService<TradeManager>();
    }

    public static void HandleDisconnect(Client client)
    {
        int disconnectedInstance = _questRoomMembers.InstanceOf(client.Id);

        ForgetConnectionState(client);
        bool travelWasArmed = client.Session.ExpectedQuestTravel;
        bool expectedCityTravel = client.Session.ExpectedCityTravelAt is long cityTravelArmedAt
            && CityManager.IsExpectedCityTravelDisconnect(pid: Game.PidOf(client), cityTravelArmedAt,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        int pid = Game.PidOf(client);
        bool selected = client.Player != null;
        client.Player = null;
        ReleaseSelection(client, pid, selected, disconnectedInstance, travelWasArmed, expectedCityTravel);
    }

    // op120 (back to character select) keeps the Game link open, so the selected character is
    // released here as a disconnect would release it, with no travel in flight.
    internal static void ReleaseForCharacterSelect(Client client)
    {
        int pid = Game.PidOf(client);
        bool selected = client.Player != null;
        client.Player = null;
        ReleaseSelection(client, pid, selected, _questRoomMembers.InstanceOf(client.Id),
            travelWasArmed: false, expectedCityTravel: false);
    }

    private static void ReleaseSelection(Client client, int pid, bool selected, int disconnectedInstance,
        bool travelWasArmed, bool expectedCityTravel)
    {
        bool disconnectedWasQuestHost = false;
        bool expectedQuestTravel = false;
        int abandonedQuestHost = 0;
        QuestInstanceManager.QuestTransport? abandonedTransport = null;
        QuestInstanceManager.QuestEntryReservation? abandonedEntryItems = null;
        if (selected)
        {
            disconnectedWasQuestHost = disconnectedInstance > 0
                && _questInstanceManager.TryGetHost(disconnectedInstance, out int hostPid)
                && hostPid == pid;
            expectedQuestTravel = QuestManager.IsExpectedQuestTravelDisconnect(pid, disconnectedInstance,
                _questInstanceManager.IsInstanceOpen(disconnectedInstance), travelWasArmed);
            ReleaseCharacterState(client, pid, expectedCityTravel);
            if (!expectedQuestTravel)
                AbandonPendingQuestRoom(pid, out abandonedQuestHost, out abandonedTransport, out abandonedEntryItems);
            LeaveSquadOnDisconnect(pid, preserve: SquadManager.ShouldPreserveSquadOnDisconnect(expectedQuestTravel, expectedCityTravel));
        }
        if (abandonedTransport != null)
            NotifyAbandonedQuestGuests(abandonedTransport, abandonedQuestHost > 0 ? abandonedQuestHost : pid, pid);
        if (abandonedEntryItems != null)
            RestoreAbandonedEntryItems(abandonedQuestHost, abandonedEntryItems);
        if (disconnectedWasQuestHost && !expectedQuestTravel)
        {
            SettleRunsAfterHostLoss(client.Id, pid, disconnectedInstance);
            _questInstanceManager.QuestHostLost(client.Id, pid, disconnectedInstance);
        }
        if (selected && !expectedQuestTravel)
            _questInstanceManager.ForgetRehostMember(pid);
        if (expectedQuestTravel)
        {
            _questRoomMembers.Detach(pid, client.Id);
            _logger.LogInformation("[Quest] retained PID {0} instance {1} across expected city-link travel disconnect.",
                pid, disconnectedInstance);
        }
        else
        {
            _questRoomMembers.ClearQuest(client.Id);
            _questInstanceManager.LeaveQuestInstance(client.Id, pid);
        }
        _questInstanceManager.AbortPendingQuestDecisionOnDisconnect(client.Id, pid);
        if (_questManager.TryTakePendingShare(client.Id, out var abandonedShare)
            && Game.TryGetClient(abandonedShare.SharerPid, out var sharerClient))
            sharerClient.SendPacket(QuestManager.BuildQuestShareDisconnectResult(pid,
                abandonedShare.SharerPid, abandonedShare.QuestIndex));
    }

    // The host's world is gone, so nobody in it sends the checkpoint/abort settlement report.
    // Credit each member's run BP and QT since the last settlement (the stored op54 totals),
    // as that report would have, before the mission restarts.
    private static void SettleRunsAfterHostLoss(int hostClientId, int hostPid, int instance)
    {
        int questIndex = _questRoomMembers.QuestOf(hostClientId);
        if (questIndex <= 0) return;
        var members = _questRoomMembers.InstanceMembers()
            .Where(member => member.Value == instance && member.Key != hostClientId)
            .Select(member => _playerManager.OnlineClients.FirstOrDefault(x => x.Id == member.Key))
            .Where(memberClient => memberClient != null)
            .Select(memberClient => Game.PidOf(memberClient!))
            .Where(memberPid => memberPid > 0)
            .Append(hostPid).Distinct();
        foreach (int memberPid in members)
        {
            PlayerStore.PlayerProgress? progress = null;
            int previousLevel = 0;
            (int BP, int QT) run = default;
            _multiStoreTransaction.Run(() =>
            {
                run = _questStore.TakeRunTotals(memberPid, questIndex);
                if (run != (0, 0))
                    run = _questInstanceManager.ClampRunReward(instance, memberPid, run.BP, run.QT,
                        otherParticipant: memberPid != hostPid,
                        questRewardBp: _questRewards.Get(memberPid, questIndex).BP);
                if (run != (0, 0))
                    progress = _playerStore.AddReward(memberPid, 0, run.BP, 0, 0, run.QT, out previousLevel);
            });
            if (progress == null) continue;
            _characterManager.PublishLiveProgression(memberPid, previousLevel, progress);
            _logger.LogInformation("[Quest] settled PID {0} quest {1} after host loss: {2:+0;-0} BP, +{3} QT.",
                memberPid, questIndex, run.BP, run.QT);
        }
    }

    // Per-connection maps keyed by client id.
    private static void ForgetConnectionState(Client client)
    {
        _simulationManager.ReleasePort(client);
        _playerManager.RemoveLink(client);
    }

    // The selected character leaves the world: presence, trade, Skirmish room, battlefield
    // reservation, position and mounted vehicle.
    private static void ReleaseCharacterState(Client client, int pid, bool expectedCityTravel)
    {
        // City travel and quest returns open a new Game link before the old one closes. An old link
        // that closes after the new one has selected and spawned the character must not take the
        // live character out of the world (DelPlayer, position, city map, tram, mount).
        bool currentLink = !Game.TryGetClient(pid, out var selected) || ReferenceEquals(selected, client);
        if (currentLink) _cityManager.LeaveCurrentPresence(client, pid);
        _tradeManager.CancelTradeOnLeave(pid);
        _simulationManager.LeaveSimulationCenter(pid);
        if (selected != null && ReferenceEquals(selected, client))
            _playerManager.SetOffline(pid);
        _battleManager.CancelReservation(pid);
        if (currentLink)
        {
            Game.PlayerFor(pid).Position = null;
            Game.PlayerFor(pid).CityMap = null;
            Game.PlayerFor(pid).TryLeaveTransport();
            // HuxleySubsystem.OnLoadMap and LoadCityZone use ClientTravel(TRAVEL_Absolute), so the
            // destination pawn has no DrivenVehicle; keeping the toggle would invert the next update.
            if (VehicleManager.ClearMountedVehicleOnPresenceExit(pid, out int mountedVehicle))
                _logger.LogInformation("[Vehicle] PID {0} disconnect clears mounted {1}; city travel {2}.",
                    pid, mountedVehicle, expectedCityTravel);
        }
        Game.PlayerFor(pid).UndoSales = null;
        _questInstanceManager.ForgetAcceptedGroup(pid);
    }

    // Entry items are reserved for the whole party under the host PID. The host leaving before
    // op199 confirms the room cancels that pending room and gives every remaining member its
    // items back. A guest leaving, before or after dispatch, only gives back its own items; the
    // host's room carries on without it.
    private static void AbandonPendingQuestRoom(int pid, out int abandonedQuestHost,
        out QuestInstanceManager.QuestTransport? abandonedTransport, out QuestInstanceManager.QuestEntryReservation? abandonedEntryItems)
    {
        abandonedTransport = null;
        abandonedEntryItems = null;
        abandonedQuestHost = 0;
        if (_questInstanceManager.AbandonDispatchedGuest(pid)) return;
        if (_questInstanceManager.AbandonPendingQuestGuest(pid)) return;
        if (_questInstanceManager.TryTakeQuestEntryReservation(pid, out abandonedEntryItems))
        {
            abandonedQuestHost = pid;
            _questInstanceManager.ForgetAcceptedGroup(pid);
        }
        _questInstanceManager.TryTakeTransport(pid, out abandonedTransport);
    }

    private static void LeaveSquadOnDisconnect(int pid, bool preserve)
    {
        List<int> squadMembers = _squadManager.LeaveOnDisconnect(pid, preserve);
        foreach (int memberPid in squadMembers) _squadManager.BroadcastSquadState(memberPid);
    }

    private static void NotifyAbandonedQuestGuests(QuestInstanceManager.QuestTransport transport, int hostPid, int leavingPid)
    {
        foreach (int memberPid in transport.Members.Where(x => x != leavingPid))
            if (Game.TryGetClient(memberPid, out var memberClient))
                foreach (var packet in QuestInstanceManager.BuildFailedQuestTransportGuestPackets(hostPid))
                    memberClient.SendPacket(packet);
    }

    private static void RestoreAbandonedEntryItems(int hostPid, QuestInstanceManager.QuestEntryReservation reservation) =>
        _multiStoreTransaction.Run(() =>
        {
            _questEntryReservationStore.Remove(hostPid);
            foreach (int memberPid in reservation.Members)
            {
                if (!_playerStore.TryExchangeQuestItems(memberPid, [], reservation.Items,
                        out var restored, out var changedCells))
                {
                    _logger.LogError("[Quest] failed to restore reserved entry items for PID {0} after host loss.", memberPid);
                    continue;
                }
                _itemManager.SynchronizeQuestPickupObjectives(memberPid, reservation.Items.Select(item => item.ItemId));
                if (Game.TryGetClient(memberPid, out var memberClient))
                    QuestManager.SendQuestInventoryChanges(memberClient, changedCells, restored);
            }
        });
}
