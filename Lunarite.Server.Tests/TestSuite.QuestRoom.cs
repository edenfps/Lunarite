using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void EntryItemConsumption()
    {
        ResetCharacter(EntryItemPid);
        _playerStore.GetInventory(EntryItemPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 5, ItemId = 609301, Count = 2 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 6, ItemId = 604221, Count = 1 }
        ]);
        _playerStore.AddItems(EntryItemPid, [(609301, 2), (604221, 1)]);
        Check(!_playerStore.TryConsumeQuestEntryItems(EntryItemPid, [(609301, 1), (609302, 1)], out _)
            && _playerStore.GetInventory(EntryItemPid, () => []).Single(x => x.ItemId == 609301).Count == 2,
            "quest entry-item consumption rolls back when any retail requirement is missing");
        Check(_playerStore.TryConsumeQuestEntryItems(EntryItemPid, [(609301, 1)], out var entryCells)
            && entryCells.SequenceEqual(new byte[] { 5 })
            && _playerStore.GetInventory(EntryItemPid, () => []).Single(x => x.ItemId == 609301).Count == 1
            && _playerStore.GetItems(EntryItemPid).Single(x => x.item == 609301).num == 1,
            "quest entry consumes the required item atomically from aggregate and slotted inventory");
    }

    private void EntryReservationRecovery()
    {
        _questEntryReservationStore.Put(9900, [EntryRefundPid], [(604221, 2)]);
        int entryRefunds = _questEntryReservationStore.RefundAbandoned();
        Check(entryRefunds == 1 && _questEntryReservationStore.All().Count == 0
            && _playerStore.GetInventory(EntryRefundPid, () => []).Where(i => i.ItemId == 604221).Sum(i => i.Count) == 2,
            "startup refunds quest entry items whose reservation was never committed");
        _questEntryReservationStore.Put(9901, [EntryRefundPid], [(604221, 1)]);
        _questEntryReservationStore.Remove(9901);
        Check(_questEntryReservationStore.All().Count == 0, "committed quest entry reservations are removed from disk");
    }

    private void EntryKeysAndCityServices()
    {
        var questEnterSession = new ClientSession();
        questEnterSession.SetQuestEnterKey(1234, 820100);
        Check(!questEnterSession.TryConsumeQuestEnterKey(1234, 0)
            && !questEnterSession.TryConsumeQuestEnterKey(999, 820100)
            && !questEnterSession.TryConsumeQuestEnterKey(1234, 820101)
            && questEnterSession.TryConsumeQuestEnterKey(1234, 820100)
            && !questEnterSession.TryConsumeQuestEnterKey(1234, 820100)
            && !new ClientSession().TryConsumeQuestEnterKey(1234, 820100),
            "op40 accepts only the op44-issued key for the same connection and quest, once");
        var infoViewShort = new PacketReader(new byte[] { 0x29, 0x08, 0, 0 });
        var infoViewLong = new PacketReader(new byte[] { 0x29, 0x08, 0, 0, 1 });
        var infoViewBad = new PacketReader(new byte[] { 0x29, 0x08, 0, 0, 1, 9 });
        Check(PlayerInfoViewRequest.TryDeserialize(ref infoViewShort, out var infoShort) && infoShort.Pid == 0x829
            && PlayerInfoViewRequest.TryDeserialize(ref infoViewLong, out var infoLong) && infoLong.Pid == 0x829
            && !PlayerInfoViewRequest.TryDeserialize(ref infoViewBad, out _),
            "op15 accepts both native senders: _SendPlayerInfoView `d` and RequestAnotherPlayerInfo `db`");

        const int cityWarpPid = 920039;
        ResetCharacter(cityWarpPid);
        _durativeItemStore.Reset(cityWarpPid);
        Check(_cityManager.ApplyCityWarpCost(cityWarpPid, 989) == 1
            && _playerStore.GetProgress(cityWarpPid).Money == 10_000,
            "city warp rejects client prices below the script-proven 990 CR minimum without charging");
        Check(_cityManager.ApplyCityWarpCost(cityWarpPid, 990) == 0
            && _playerStore.GetProgress(cityWarpPid).Money == 9_010,
            "city warp atomically charges a valid client-computed distance price");
        Check(_cityManager.ApplyCityWarpCost(cityWarpPid, -1) == 1,
            "city warp rejects the pass sentinel without its active durative entitlement");
        _durativeItemStore.ReplaceAll(cityWarpPid,
        [
            new DurativeItem { ItemID = 678202, ExpirationDate = int.MaxValue, CityItemGroup = 15 }
        ]);
        Check(_cityManager.ApplyCityWarpCost(cityWarpPid, -1) == 0
            && _playerStore.GetProgress(cityWarpPid).Money == 9_010,
            "city warp accepts the script-proven group-15 pass without charging CR");
        _durativeItemStore.Reset(cityWarpPid);

        const int itemStatePid = 920040;
        ResetCharacter(itemStatePid);
        _licenseStore.Reset(itemStatePid);
        Check(_licenseStore.Grant(itemStatePid, 723703)
            && _licenseStore.TuneupDiscountPercent(itemStatePid) == 20,
            "highest owned TuneupTalent exposes its exported 20-percent city-license discount");
        Check(_licenseStore.Grant(itemStatePid, 723723)
            && _licenseStore.RepairDiscountPercent(itemStatePid) == 20,
            "highest owned RepairTalent exposes its exported 20-percent city-license discount");
        _durativeItemStore.ReplaceAll(itemStatePid,
        [
            new DurativeItem { ItemID = 670999, OptionValue = 723713,
                ExpirationDate = int.MaxValue, CityItemGroup = 5 }
        ]);
        Check(_licenseStore.AuctionDiscountPercent(itemStatePid) == 20
            && !_licenseStore.Has(itemStatePid, 723713),
            "active term city talents participate in retail city-service rates without becoming permanent");
        _durativeItemStore.Reset(itemStatePid);
        _playerStore.GetInventory(itemStatePid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 128403, State = 1, TuneUpOptions = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 2, ItemId = 673001, Count = 2 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 3, ItemId = 500001, Count = 100 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 672001, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 5, ItemId = 100298, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 6, ItemId = 109201, Count = 1, Durability = 100 }
        ]);
        long beforePcRoomTune = _playerStore.GetProgress(itemStatePid).Money;
        Check(_playerStore.TryTuneUp(itemStatePid, 6, _licenseStore.TuneupDiscountPercent(itemStatePid), out _, out int pcRoomOldTune, out int pcRoomNewTune) == 9
            && pcRoomOldTune == 0 && pcRoomNewTune == 0
            && _playerStore.GetProgress(itemStatePid).Money == beforePcRoomTune,
            "tune-up returns native PC-room result without consuming currency or materials");
        Check(_playerStore.TryTuneUp(itemStatePid, 1, _licenseStore.TuneupDiscountPercent(itemStatePid), out byte tunedOption, out int oldTune, out int newTune,
                optionOverride: 6) == 0
            && tunedOption == 6 && oldTune == 1 && newTune == 0x01000001
            && _playerStore.GetProgress(itemStatePid).Money == 7600
            && _playerStore.GetInventory(itemStatePid, () => []).Single(x => x.ItemId == 500001).Count == 72,
            "tune-up applies the script-proven city-license fee multiplier, charges retail lunarites, and advances an eligible option");
        Check(_playerStore.TryTuneUp(itemStatePid, 5, _licenseStore.TuneupDiscountPercent(itemStatePid), out _, out int disabledOldTune, out int disabledNewTune) == 3
            && disabledOldTune == 0 && disabledNewTune == 0,
            "items whose seven exported tune-up category caps are zero reject tuning without mutation");
        Check(_playerStore.TryRemoveItemBind(itemStatePid, 2, 1, out var unbound)
            && unbound is { State: 0x10 }
            && _playerStore.GetInventory(itemStatePid, () => []).Single(x => x.InventoryCell == 2).Count == 1,
            "bind reset consumes one tool and persists the reset count in byState");
        Check(_playerStore.TryResetTuneUp(itemStatePid, 4, 1, 1, 0, out int beforeReset, out int afterReset) == 0
            && beforeReset != 0 && afterReset == 0
            && _playerStore.GetInventory(itemStatePid, () => []).All(x => x.InventoryCell != 4),
            "full tune reset validates retail item 672001, consumes it, and clears packed options");

        const int partialResetPid = 920041;
        ResetCharacter(partialResetPid);
        _playerStore.GetInventory(partialResetPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 128403, State = 1, TuneUpOptions = 3 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 2, ItemId = 672002, Count = 2 }
        ]);
        Check(_playerStore.TryResetTuneUp(partialResetPid, 2, 1, 0, 0,
                out int partialBefore, out int partialAfter) == 0
            && partialBefore == 3 && partialAfter == 2
            && _playerStore.GetInventory(partialResetPid, () => []).Single(x => x.InventoryCell == 2).Count == 1,
            "partial tune reset consumes 672002 and decreases the selected option by exactly one level");
        Check(_playerStore.TryResetTuneUp(partialResetPid, 99, 1, 0, 0, out _, out _) == 2
            && _playerStore.TryResetTuneUp(partialResetPid, 1, 99, 0, 0, out _, out _) == 3,
            "tune reset returns native missing-use-item and missing-destination result codes");
        Check(_playerStore.TryResetTuneUp(partialResetPid, 2, 1, 0, 1, out _, out _) == 5,
            "partial tune reset returns the native selected-option-is-zero result without consuming its tool");
        var acquireWireQuest = new QuestAcquireList.AcquiredQuest
        {
            Index = 820400,
            Complete = 2,
            Objectives = new QuestAcquireList.ObjectiveInfo[8]
        };
        acquireWireQuest.Objectives[0] = new QuestAcquireList.ObjectiveInfo
            { Count = 3, Complete = 1, Visible = 0 };
        var acquireWire = new QuestAcquireList { Quests = [acquireWireQuest] }.Serialize();
        Check(acquireWire.Length == 34 && acquireWire[0] == 38
            && BitConverter.ToInt16(acquireWire, 1) == 1
            && BitConverter.ToInt32(acquireWire, 3) == 820400
            && acquireWire[10] == 3 && acquireWire[11] == 1 && acquireWire[12] == 0,
            "quest journal serializes each objective as count, completion flag, and visibility");
        Check(_questStore.Acquire(QuestPid, QuestIndex), "quest journal acquires a quest");
        Check(_questInstanceManager.IsQuestLootRequestAuthorized(QuestPid, QuestIndex, 1)
            && !_questInstanceManager.IsQuestLootRequestAuthorized(QuestPid, QuestIndex, 0)
            && !_questInstanceManager.IsQuestLootRequestAuthorized(QuestPid, 0, 1)
            && !_questInstanceManager.IsQuestLootRequestAuthorized(QuestPid + 1, QuestIndex, 1),
            "quest loot requests require the requesting PID's active acquired instance");
        _directFieldQuest = _questCatalog.All.First(x => x.QuestType == "QuestType_Free").EntryIndex;
        Check(_questInstanceManager.IsQuestLootRequestAuthorized(QuestPid + 1, _directFieldQuest, 77)
            && !_questInstanceManager.IsQuestLootRequestAuthorized(QuestPid + 1, _directFieldQuest, 0),
            "direct field loot trusts the validated live instance without requiring journal acquisition");
        Check(QuestManager.ShouldReturnQuestMemberOnHostExit(10, 1001, 1001, true, 11, 1001)
            && !QuestManager.ShouldReturnQuestMemberOnHostExit(10, 1001, 1001, false, 11, 1001)
            && !QuestManager.ShouldReturnQuestMemberOnHostExit(10, 1001, 1001, true, 10, 1001)
            && !QuestManager.ShouldReturnQuestMemberOnHostExit(10, 1001, 1001, true, 11, 2002),
            "quest host exit returns only other clients bound to that live instance");
        Check(QuestManager.IsExpectedQuestTravelDisconnect(1001, 1001, true, true)
            && !QuestManager.IsExpectedQuestTravelDisconnect(1001, 0, true, true)
            && !QuestManager.IsExpectedQuestTravelDisconnect(1001, 1001, false, true)
            && !QuestManager.IsExpectedQuestTravelDisconnect(1001, 1001, true, false),
            "quest-world travel disconnect retains state only for a bound live instance");
        Check(CityManager.IsExpectedCityTravelDisconnect(1001, 1_000_000, 1_120_000)
            && CityManager.IsExpectedCityTravelDisconnect(1001, 1_000_000, 1_000_000)
            && !CityManager.IsExpectedCityTravelDisconnect(0, 1_000_000, 1_000_001)
            && !CityManager.IsExpectedCityTravelDisconnect(1001, 1_000_000, 1_120_001)
            && !CityManager.IsExpectedCityTravelDisconnect(1001, 1_000_000, 999_999),
            "city travel disconnect preservation is PID-bound and expires after the loading window");
        Check(SquadManager.ShouldPreserveSquadOnDisconnect(true, false)
            && SquadManager.ShouldPreserveSquadOnDisconnect(false, true)
            && !SquadManager.ShouldPreserveSquadOnDisconnect(false, false),
            "squads survive only explicit quest or city travel disconnects");
        const int mountedTravelPid = 920078;
        var mountedTravelState = _playerManager.Get(mountedTravelPid);
        mountedTravelState.SummonedVehicle = 608001;
        _cityManager.LeaveCurrentPresence(null!, mountedTravelPid);
        Check(!mountedTravelState.SummonedVehicle.HasValue,
            "presence exit clears a mount before an absolute city-travel observer snapshot");
        mountedTravelState.SummonedVehicle = 608001;
        Check(VehicleManager.ClearMountedVehicleOnPresenceExit(mountedTravelPid, out int clearedVehicle)
            && clearedVehicle == 608001 && !mountedTravelState.SummonedVehicle.HasValue,
            "presence-exit mount cleanup returns the prior vehicle for disconnect diagnostics");
        Check(QuestManager.CanEnterQuestByState(false, "QuestType_Free", true)
            && QuestManager.CanEnterQuestByState(true, "QuestType_Battle", false)
            && !QuestManager.CanEnterQuestByState(false, "QuestType_FreeQuest", true)
            && !QuestManager.CanEnterQuestByState(false, "QuestType_Free", false),
            "available field/scout missions allow direct entry while ordinary quests still require acquisition");
        Check(QuestManager.AreQuestPartyMembersEligible([7, 9], pid => pid is 7 or 9)
            && QuestManager.AreQuestPartyMembersEligible([7, 7, 9], pid => pid is 7 or 9)
            && !QuestManager.AreQuestPartyMembersEligible([7, 9], pid => pid == 7)
            && !QuestManager.AreQuestPartyMembersEligible([], _ => true),
            "cooperative quest creation revalidates the accepted party independently of listen-host ownership");
        Check(QuestInstanceManager.IsQuestDifficultyValid(0) && QuestInstanceManager.IsQuestDifficultyValid(1)
            && QuestInstanceManager.IsQuestDifficultyValid(2) && !QuestInstanceManager.IsQuestDifficultyValid(3)
            && !QuestInstanceManager.IsQuestDifficultyValid(byte.MaxValue),
            "quest entry accepts exactly the retail normal, expert, and master difficulty values");
        Check(QuestManager.IsQuestRallyTelemetryValid(0, 0, 0, 0, 124, 0, 17, 20, 129)
            && QuestManager.IsQuestRallyTelemetryValid(0, 0, 0, 0, -1, 0, 17, 20, 129)
            && !QuestManager.IsQuestRallyTelemetryValid(2, 1, 0, 0, 124, 0, 17, 20, 129)
            && !QuestManager.IsQuestRallyTelemetryValid(0, 0, 0, -1, 124, 0, 17, 20, 129),
            "direct field settlement accepts signed retail BP and rejects impossible counters");
        Check(QuestInstanceManager.IsDirectFieldSettlement(true, true, 0, 0, 0, 35, 67)
            && QuestInstanceManager.IsDirectFieldSettlement(true, true, 0, 0, 0, -5, 0)
            && !QuestInstanceManager.IsDirectFieldSettlement(true, true, 0, 0, 0, 0, 0)
            && !QuestInstanceManager.IsDirectFieldSettlement(true, true, 1, 2, 0, 35, 67)
            && !QuestInstanceManager.IsDirectFieldSettlement(true, true, 1, 2, 2, 35, 67)
            && !QuestInstanceManager.IsDirectFieldSettlement(true, true, 0, 0, 2, 35, 67)
            && !QuestInstanceManager.IsDirectFieldSettlement(false, true, 0, 0, 0, 35, 67)
            && !QuestInstanceManager.IsDirectFieldSettlement(true, false, 0, 0, 0, 35, 67),
            "direct field BP/QT settle from retail rally-0/0 UNKNOWN checkpoint reports only");
        const int missionPenaltyPid = 920122;
        var missionPenaltyProgress = _playerStore.GetProgress(missionPenaltyPid);
        missionPenaltyProgress.BP = PlayerStore.ComputePlayerLevelupGoalBP(1) + 20;
        long missionPenaltyFloor = PlayerStore.ComputePlayerLevelupGoalBP(1) + 1L;
        Check(_playerStore.CanApplyMissionBp(missionPenaltyPid,
                checked((int)(missionPenaltyFloor - missionPenaltyProgress.BP)))
            && !_playerStore.CanApplyMissionBp(missionPenaltyPid,
                checked((int)(missionPenaltyFloor - missionPenaltyProgress.BP - 1))),
            "mission death BP penalties preserve the retail current-level floor");
        Check(_rankTable.RankFor(0, 0) == 0 && _rankTable.RankFor(10, 0) == 1 && _rankTable.RankFor(30, 0) == 3
            && _rankTable.RankFor(45, 0) == 4 && _rankTable.RankFor(885, 0) == 27 && _rankTable.RankFor(955, 0) == 27
            && _rankTable.RankFor(955, 2) == 28 && _rankTable.RankFor(3045, 120) == 51
            && _rankTable.For(51) == new RankTable.Requirement(3045, 120)
            && RankTable.GroupStartsCrossed(0, 5) == 2 && RankTable.IsGroupStart(44) && !RankTable.IsGroupStart(45),
            "rank thresholds follow the client's cumulative A/B medal table");
        const int rankPid = 920460;
        var rankProgress = _playerStore.GetProgress(rankPid);
        rankProgress.Rank = 0; rankProgress.AMedalsDonated = 50; rankProgress.BMedalsDonated = 0;
        rankProgress.BP = PlayerStore.ComputePlayerLevelupGoalBP(29) + 1;
        long rankLpBefore = _playerStore.GetProgress(rankPid).LP; // settles level-30 LP first
        bool rankPromoted = _playerStore.TryPromoteRank(rankPid, out byte rankOld, out byte rankNew, out int rankLpGain);
        var lowLevelRank = _playerStore.GetProgress(920461);
        lowLevelRank.AMedalsDonated = 50;
        Check(rankPromoted && rankOld == 0 && rankNew == 4 && rankLpGain == 2
            && _playerStore.GetProgress(rankPid).LP == rankLpBefore + 2
            && !_playerStore.TryPromoteRank(rankPid, out _, out _, out _)
            && !_playerStore.TryPromoteRank(920461, out _, out _, out _),
            "medal donations promote only at level 30, to the highest met rank, +1 LP per new named rank");
        Check(!_questStore.ReadyToComplete(QuestPid, QuestIndex),
            "newly acquired quest cannot be completed before an authoritative instance result");
        Check(_questStore.UpdateObjective(QuestPid, QuestIndex, 1, 7, true), "objective report persists");
        Check(!_questStore.UpdateObjective(QuestPid, QuestIndex, 3, 1, true)
            && !_questStore.UpdateObjective(QuestPid, QuestIndex, 1, 11, true),
            "objective reports reject unknown indices and counts above the retail completion target");
        Check(_questStore.UpdateRally(QuestPid, QuestIndex, 1, 2, 2, 90, 100, 200, 3, 4, 5000),
            "rally report persists");
        Check(!_questStore.UpdateRally(QuestPid, QuestIndex, 3, 2, 2)
            && !_questStore.UpdateRally(QuestPid, QuestIndex, 1, 2, 4)
            && _questStore.UpdateRally(QuestPid, QuestIndex, 1, 2, 2, bp: -1)
            && _questStore.GetProgress(QuestPid, QuestIndex).BP == -1
            && _questStore.UpdateRally(QuestPid, QuestIndex, 1, 2, 2, 90, 100, 200, 3, 4, 5000),
            "rally reports reject impossible progress and completion enums while preserving signed BP");
        _questProgress = _questStore.GetProgress(QuestPid, QuestIndex);
        Check(_questProgress.RallyPoint == 1 && _questProgress.MaxRallyPoint == 2 && _questProgress.Complete == 2
            && _questStore.ReadyToComplete(QuestPid, QuestIndex),
            "quest rally state round-trips");
        Check(_questProgress is { SquadBP: 90, BP: 100, QT: 200, KilledMonsters: 3,
                SpawnedMonsters: 4, PlayTime: 5000 },
            "quest rally BP/QT/combat telemetry round-trips for completion rewards");
    }
}
