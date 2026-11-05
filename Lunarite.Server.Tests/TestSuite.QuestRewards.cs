using System.Buffers.Binary;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void RewardGraphsAndEntryRules()
    {
        Check(_questCatalog.Audit() is
            {
                QuestObjects: 530, RewardStages: 454, NonPrimaryRewardStages: 1,
                ResultDropQuests: 0, ExtractionErrors: 0
            },
            "canonical quest dump recovers staged rewards and proves random result drops are unused");
        var sapiensForestReward = _questRewards.GetForSpecies(1, 820502);
        var alternativesForestReward = _questRewards.GetForSpecies(11, 870502);
        Check(sapiensForestReward.BP == 500 && sapiensForestReward.Money == 500
            && sapiensForestReward.LicensePoints == 0 && sapiensForestReward.Items.Count == 0,
            "Sapiens forest completion uses the exported 820502 reward graph");
        Check(alternativesForestReward.BP == 500 && alternativesForestReward.Money == 500
            && alternativesForestReward.LicensePoints == 0 && alternativesForestReward.Items.Count == 0,
            "Alternatives forest completion uses the exported 870502 reward graph");
        Check(_questRewards.GetForSpecies(11, 871906) is { BP: 2250, Money: 2750, LicensePoints: 0 },
            "the exported reward builder includes quest 871906's Name=None completion transaction");
        var rewardAudit = _questRewards.Audit();
        Check(rewardAudit is { QuestObjects: 530, RewardEntries: 457, Modifiers: 765,
                OptionalGroups: 70, OptionalChoices: 210, InvalidQuantities: 0, UnknownItems: 0,
                ExtractionErrors: 0, CompletionModifierReferences: 765, MissingModifiers: 0,
                NonCompletionModifiers: 0 },
            "all exported quest rewards bind to completion transaction links without missing or extra modifiers");
        var optionalSelection = new QuestRewards.Reward
        {
            OptionalItems = [(110212, 1), (110405, 1), (110811, 1)]
        };
        Check(!QuestRewards.TrySelectOptional(optionalSelection, -1)
            && !QuestRewards.TrySelectOptional(optionalSelection, 3)
            && QuestRewards.TrySelectOptional(optionalSelection, 1)
            && optionalSelection.Items.SequenceEqual(new[] { (110405, 1) }),
            "optional quest rewards reject INDEX_NONE/out-of-range slots and add exactly one selected item");
        Check(_questCatalog.Get(846004)?.EnterRequestItems is [{ ItemIndex: 609301, ItemCount: 1 }]
            && _questCatalog.Get(846105)?.EnterRequestItems is [{ ItemIndex: 609302, ItemCount: 1 }],
            "retail quest catalog decodes exact consumable entry-item requirements");
        var unresolvedQuestItems = _questCatalog.All
            .SelectMany(quest => quest.EnterRequestItems.Select(item => item.ItemIndex)
                .Concat(quest.QuestItems).Concat(quest.RemoveItems))
            .Distinct().Where(itemId => _itemCatalog.Get(itemId) == null).Order().ToArray();
        Check(unresolvedQuestItems.Length == 0,
            $"all cooked quest entry, acquisition, and removal items resolve to inventory definitions ({string.Join(',', unresolvedQuestItems)})");
        var invalidEntryStacks = _questCatalog.All.SelectMany(quest => quest.EnterRequestItems
                .Where(item => item.ItemCount <= 0 || _itemCatalog.Get(item.ItemIndex) is not { MaxCollapseCount: > 0 } data
                    || item.ItemCount > data.MaxCollapseCount)
                .Select(item => (quest.EntryIndex, item.ItemIndex, item.ItemCount))).ToArray();
        Check(invalidEntryStacks.Length == 0,
            $"all cooked quest-entry costs fit a valid single inventory stack ({string.Join(',', invalidEntryStacks)})");
        Check(QuestInstanceManager.ValidateQuestPartySize(_questCatalog.Get(822905), 1) == 9
            && QuestInstanceManager.ValidateQuestPartySize(_questCatalog.Get(822905), 2) == 0
            && QuestInstanceManager.ValidateQuestPartySize(_questCatalog.Get(822905), 4) == 0
            && QuestInstanceManager.ValidateQuestPartySize(_questCatalog.Get(822905), 5) == 10,
            "retail cooperative quest limits return exact deficiency and exceed results");
        Check(QuestInstanceManager.ValidateQuestPartySize(_questCatalog.Get(820100), 1) == 0
            && QuestInstanceManager.ValidateQuestPartySize(_questCatalog.Get(820100), 2) == 10,
            "retail solo quest maximum rejects an oversized party");
        Check(QuestManager.AreQuestPartyMembersOnline([101, 102], [102, 101, 999])
            && !QuestManager.AreQuestPartyMembersOnline([101, 102], [101, 999])
            && !QuestManager.AreQuestPartyMembersOnline([], [101, 102])
            && !QuestManager.AreQuestPartyMembersOnline([0, 101], [0, 101]),
            "quest room creation rejects empty, invalid, or disconnected accepted parties");
        Check(QuestInstanceManager.ValidateQuestEquipment([]) == 11
            && QuestInstanceManager.ValidateQuestEquipment([new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 203001, Durability = 100 }]) == 12
            && QuestInstanceManager.ValidateQuestEquipment([
                new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 203001, Durability = 100 },
                new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100101, Durability = 0 }]) == 13
            && QuestInstanceManager.ValidateQuestEquipment([
                new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 203001, Durability = 100 },
                new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100101, Durability = 100 }]) == 0,
            "retail mission equipment gate returns exact armor, weapon, and durability results");
        Check(_questCatalog.Get(820301)?.QuestItems.SequenceEqual(new[] { 820301 }) == true
            && _questCatalog.Get(820301)?.RemoveItems.SequenceEqual(new[] { 820301 }) == true,
            "retail quest catalog decodes mission-carried and removal item lists");
        Check(_questCatalog.Get(820102)?.Objectives.Select(x => (x.Index, x.CompleteCount))
                .SequenceEqual(new[] { (0, 1), (1, 10), (2, 1) }) == true,
            "retail quest catalog decodes exact objective indices and completion counts");
        Check(_questCatalog.Get(820201)?.Objectives is
                [{ ObjectiveType: "QuestObjectiveType_Primary", BattleZoneGameType: "BQOGameType_SimulationBattle",
                   BattleZoneObjective: "BQOType_TDM" }],
            "retail quest catalog retains primary Simulation TDM objective semantics");
        const int simulationQuestPid = 920093;
        _questStore.Reset(simulationQuestPid);
        Check(_questStore.Acquire(simulationQuestPid, 820201)
            && _questStore.RecordBattleZoneMatch(simulationQuestPid, true, 1) is
                [{ QuestIndex: 820201, ObjectiveIndex: 0, Count: 1 }]
            && _questStore.ReadyToComplete(simulationQuestPid, 820201)
            && _questStore.RecordBattleZoneMatch(simulationQuestPid, true, 1).Count == 0,
            "authenticated Simulation TDM result advances its BattleZone quest exactly once");
        _questStore.Reset(simulationQuestPid);
        const int battleFallbackPid = 920094;
        _questStore.Reset(battleFallbackPid);
        Check(_questStore.Acquire(battleFallbackPid, 822913)
            && _questStore.RecordBattleZoneMatch(battleFallbackPid, true, 6, false, 7) is
                [{ QuestIndex: 822913, ObjectiveIndex: 0, Count: 7 }]
            && _questStore.GetProgress(battleFallbackPid, 822913).Objectives[0].Count == 7,
            "authenticated TDDM telemetry advances aggregate Simulation Battle kill objectives by its exact kill count");
        _questStore.Reset(battleFallbackPid);
        Check(_questStore.Acquire(battleFallbackPid, 823012)
            && _questStore.RecordBattleZoneMatch(battleFallbackPid, false, 1, true, 0) is
                [{ QuestIndex: 823012, ObjectiveIndex: 0, Count: 1 }]
            && _questStore.ReadyToComplete(battleFallbackPid, 823012),
            "authenticated winning Battle telemetry completes retail victory objectives");
        _questStore.Reset(battleFallbackPid);
        var postForestCharacter = _characterStore.Create(930820502, "PostForestGate", 1, new Custom());
        _characterStore.SetStyle(postForestCharacter.Pid, 1);
        ResetCharacter(postForestCharacter.Pid);
        _playerStore.AddReward(postForestCharacter.Pid, 0, 100_000, 0);
        _questStore.Reset(postForestCharacter.Pid);
        _questStore.Acquire(postForestCharacter.Pid, 820502);
        _questStore.Complete(postForestCharacter.Pid, 820502);
        var postForestAvailable = _questConditions.Available(postForestCharacter.Pid);
        Check(postForestAvailable.Contains(820600) && postForestAvailable.Contains(820630)
            && !postForestAvailable.Contains(820503)
            && _questCatalog.Get(820630) is { QuestType: "QuestType_Free", Level: "FQ-820630" }
            && _questCatalog.ClientHasMap("FQ-820630") && _questCatalog.ClientHasMap("Q-820600")
            && _questCatalog.Get(820600)?.Objectives.Count == 6
            && _questCatalog.Get(820600)?.Objectives[0].ObjectiveType == "QuestObjectiveType_Primary",
            "completing forest quest 820502 unlocks its exact exported scout and battle-mission branches");
        var directFieldDefinitions = _questCatalog.All.Where(x => x.QuestType == "QuestType_Free").ToArray();
        Check(directFieldDefinitions.Length == 71 && directFieldDefinitions.All(x => x.Objectives.Count == 0),
            "all exported direct-entry field quests have no op55 objective records");
        Check(directFieldDefinitions.All(quest =>
            new byte[] { 1, 11 }.All(species =>
            {
                var reward = _questRewards.GetForSpecies(species, quest.EntryIndex);
                return reward.BP == 0 && reward.Money == 0 && reward.LicensePoints == 0
                    && reward.Items.Count == 0 && reward.OptionalItems.Count == 0;
            })),
            "direct-entry field quests use reported run BP/QT without additional cooked completion rewards");
        _questStore.Reset(postForestCharacter.Pid);
        var simultaneousLootLookups = new bool[32];
        Parallel.For(0, simultaneousLootLookups.Length, i =>
            simultaneousLootLookups[i] = _questLoot.EnemyCanDrop(9050111, 603001, 0));
        Check(simultaneousLootLookups.All(x => x),
            "parallel first-use loot lookups all observe the completed retail drop catalog");
        Check(_questCatalog.Get(820401)?.Objectives is
                [{ BattleZoneObjective: "FQOType_PickItem", CompleteCount: 3, ExtraParam: 603001 }]
            && _questLoot.EnemyCanDrop(9050111, 603001, 0),
            "quest 820401 fighter components have an exact Normal-difficulty enemy drop source");
    }

    private void DirectFieldSettlement()
    {
        BinaryPrimitives.WriteInt32LittleEndian(_directFieldRally.AsSpan(5), _detachedReportQuest);
        _directFieldRally[9] = 1; // rally point
        _directFieldRally[10] = 2; // max rally point
        _directFieldRally[11] = 1; // QUESTCOMPLETE_PLAYING, no reward yet
        int[] directFieldRallyValues = [30, 120, 7, 0, 3, 48];
        for (int i = 0; i < directFieldRallyValues.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(_directFieldRally.AsSpan(12 + i * 4), directFieldRallyValues[i]);
        Game.OnHandlePacket(_questSocketClient, _directFieldRally);
        var fieldRallyProgress = _questStore.GetProgress(OfflineHostPid, _detachedReportQuest);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer { Opcode = 56, Answer = 0,
                QuestIndex = _detachedReportQuest, Index = 1, Count = 2, State = 1 }.Serialize())
            && fieldRallyProgress is { RallyPoint: 1, MaxRallyPoint: 2, Complete: 1, SquadBP: 30,
                BP: 120, QT: 7, KilledMonsters: 1, SpawnedMonsters: 3, PlayTime: 48 },
            "authenticated direct-field op54 retains full rally telemetry without a journal acquisition");
        long failedFieldBpBefore = _playerStore.GetProgress(OfflineHostPid).BP;
        long failedFieldQtBefore = _playerStore.GetProgress(OfflineHostPid).QT;
        _directFieldRally[9] = 0;
        _directFieldRally[10] = 0;
        Check(_playerStore.GetProgress(OfflineHostPid).BP == failedFieldBpBefore
            && _playerStore.GetProgress(OfflineHostPid).QT == failedFieldQtBefore,
            "direct-field rally report (running total) credits no BP or QT");
        _directFieldRally[11] = 0; // checkpoint/QuestAbort settlement: rally 0/0, UNKNOWN
        Game.OnHandlePacket(_questSocketClient, _directFieldRally);
        Check(ReadQuestProgressAnswerPacket().SequenceEqual(new QuestProgressAnswer { Opcode = 56, Answer = 0,
                QuestIndex = _detachedReportQuest, Index = 0, Count = 0, State = 0 }.Serialize())
            && _questStore.GetProgress(OfflineHostPid, _detachedReportQuest).Complete == 1
            && _questStore.GetProgress(OfflineHostPid, _detachedReportQuest).BP == 0
            && _playerStore.GetProgress(OfflineHostPid).BP == failedFieldBpBefore + 120
            && _playerStore.GetProgress(OfflineHostPid).QT == failedFieldQtBefore + 7,
            "direct-field settlement op54 credits the BP/QT earned since the last checkpoint");
        BinaryPrimitives.WriteInt32LittleEndian(_directFieldRally.AsSpan(12 + 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(_directFieldRally.AsSpan(12 + 8), 0);
        Game.OnHandlePacket(_questSocketClient, _directFieldRally);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer { Opcode = 56, Answer = 0,
                QuestIndex = _detachedReportQuest, Index = 0, Count = 0, State = 0 }.Serialize())
            && _playerStore.GetProgress(OfflineHostPid).BP == failedFieldBpBefore + 120
            && _playerStore.GetProgress(OfflineHostPid).QT == failedFieldQtBefore + 7,
            "repeat settlement after the client zeroed its run BP adds nothing");
        BinaryPrimitives.WriteInt32LittleEndian(_directFieldRally.AsSpan(12 + 4), 120);
        BinaryPrimitives.WriteInt32LittleEndian(_directFieldRally.AsSpan(12 + 8), 7);
        var classUpProgress = new PlayerStore.PlayerProgress { Level = 10, BP = 9000, LP = 13, QT = 50 };
        _characterManager.PublishLiveProgression(OfflineHostPid, 9, classUpProgress);
        Check(ReadQuestSocketPacket().SequenceEqual(new Parameter { Param = Parameter.PARAMETER_LEVEL, Value = 10 }.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(new Parameter { Param = Parameter.PARAMETER_BP, Value = 9000 }.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(new Parameter { Param = Parameter.PARAMETER_LP, Value = 13 }.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(new Parameter { Param = Parameter.PARAMETER_QT, Value = 50 }.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(new WorldEvent { Pid = OfflineHostPid, Event = 0x01, Value = 10 }.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(new WorldEvent { Pid = OfflineHostPid, Event = 0x02, Value = 1 }.Serialize()),
            "live BP grant (PvP result) publishes parameters then retail level-up and class-up events");
        BinaryPrimitives.WriteInt32LittleEndian(_directFieldRally.AsSpan(1), OfflineGuestPid);
        _directFieldRally[9] = 1;
        _directFieldRally[10] = 2;
        _directFieldRally[11] = 2; // QUESTCOMPLETE_REWARD rally report: running total, not a settlement
        _detachedGuestBpBefore = _playerStore.GetProgress(OfflineGuestPid).BP;
        _detachedGuestQtBefore = _playerStore.GetProgress(OfflineGuestPid).QT;
        _offlineHostBpAfterSettlement = _playerStore.GetProgress(OfflineHostPid).BP;
        Game.OnHandlePacket(_questSocketClient, _directFieldRally);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer { Opcode = 56, Answer = 0,
                QuestIndex = _detachedReportQuest, Index = 1, Count = 2, State = 2 }.Serialize())
            && _playerStore.GetProgress(OfflineGuestPid).BP == _detachedGuestBpBefore,
            "direct-field REWARD rally report does not credit its running total");
        _directFieldRally[9] = 0;
        _directFieldRally[10] = 0;
        _directFieldRally[11] = 0;
    }

    private void OptionalRewardQuest()
    {
        var optionalQuestReward = _questRewards.Get(_optionalQuestPid, OptionalQuestIndex);
        Check(optionalQuestReward.OptionalItems.Count == 3
            && optionalQuestReward.OptionalItems[1] == (231205, 1)
            && _questStore.Acquire(_optionalQuestPid, OptionalQuestIndex)
            && _questStore.UpdateRally(_optionalQuestPid, OptionalQuestIndex, 0, 0, 2),
            "cooked optional-reward quest is ready for a gateway completion transaction");
        byte[] optionalCompletionWire = new byte[6];
        optionalCompletionWire[0] = 45;
        BinaryPrimitives.WriteInt32LittleEndian(optionalCompletionWire.AsSpan(1), OptionalQuestIndex);
        optionalCompletionWire[5] = byte.MaxValue;
        Game.OnHandlePacket(_questSocketClient, optionalCompletionWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestComplete
                { Answer = QuestAcquire.DontSelectItem, QuestIndex = OptionalQuestIndex }.Serialize())
            && _questStore.Acquired(_optionalQuestPid).Contains(OptionalQuestIndex)
            && _playerStore.GetInventory(_optionalQuestPid, () => []).Count == 0,
            "encrypted op45 refuses a missing optional choice without settling the quest or minting an item");
        optionalCompletionWire[5] = 1;
        Game.OnHandlePacket(_questSocketClient, optionalCompletionWire);
        byte[] optionalAnswerWire = ReadQuestSocketPacket();
        Check(optionalAnswerWire.SequenceEqual(new QuestComplete
                { Answer = 0, QuestIndex = OptionalQuestIndex }.Serialize())
            && _questStore.Completed(_optionalQuestPid).Contains(OptionalQuestIndex)
            && _playerStore.GetInventory(_optionalQuestPid, () => []).Count(x => x.ItemId == 231205) == 1
            && _playerStore.GetInventory(_optionalQuestPid, () => []).All(x => x.ItemId is not 230205 and not 232205),
            $"encrypted op45 settles exactly the selected cooked optional reward for its character (wire={Convert.ToHexString(optionalAnswerWire)}, completed={_questStore.Completed(_optionalQuestPid).Contains(OptionalQuestIndex)}, items={string.Join(',', _playerStore.GetInventory(_optionalQuestPid, () => []).Select(x => x.ItemId))})");
        _economyRates.ResetNeutral();
        _playerManager.Get(_cityQuestWirePid).CityMap = null;
        _questRoomMembers.TryTakeDetached(OfflineGuestPid, out _);
        _questRoomMembers.ClearQuest(_detachedGuestClientId);
        _questRoomMembers.TryClearInstance(_detachedGuestClientId, out _);
        _questSocketClient.Player = null;
        _squadManager.RemoveSquad(920007);
    }

    private void RallyWireFormat()
    {
        byte[] rallyWire = new byte[35];
        BinaryPrimitives.WriteInt32LittleEndian(rallyWire, 920001);
        BinaryPrimitives.WriteInt32LittleEndian(rallyWire.AsSpan(4), 820102);
        rallyWire[8] = 1;
        rallyWire[9] = 2;
        rallyWire[10] = 1;
        int[] rallyInts = [90, 100, 200, 3, 4, 5000]; // squadBP, BP, QT, kills, spawns, play time
        for (int i = 0; i < rallyInts.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(rallyWire.AsSpan(11 + i * 4), rallyInts[i]);
        var rallyReader = new PacketReader(rallyWire);
        Check(QuestListenRallyPointRequest.TryDeserialize(ref rallyReader, out var rallyReport)
            && rallyReport.Pid == 920001 && rallyReport.QuestIndex == 820102
            && rallyReport.RallyPoint == 1 && rallyReport.MaxRallyPoint == 2 && rallyReport.Complete == 1
            && rallyReport.SquadBP == 90 && rallyReport.BP == 100 && rallyReport.QT == 200
            && rallyReport.KilledMonsters == 3 && rallyReport.SpawnedMonsters == 4 && rallyReport.PlayTime == 5000,
            "native op54 dd/byte/byte/byte/6d rally wire order parses exactly");
        Check(new QuestProgressAnswer { Opcode = 56, Answer = 0, QuestIndex = 820102,
                Index = 1, Count = 2, State = 1 }.Serialize()
            .SequenceEqual(new byte[] { 56, 0, 0x86, 0x83, 0x0C, 0, 1, 2, 1 }),
            "server op56 closes the native rally transaction with exact bdbbb order");
        var invalidRallyReader = new PacketReader(rallyWire.Concat(new byte[] { 0 }).ToArray());
        Check(!QuestListenRallyPointRequest.TryDeserialize(ref invalidRallyReader, out _),
            "op54 parser rejects trailing bytes instead of silently changing protocol shape");
    }

    private void JournalSettlement()
    {
        const int settlementPid = 920450;
        _questStore.Reset(settlementPid);
        _questStore.Acquire(settlementPid, QuestIndex);
        _questStore.UpdateRally(settlementPid, QuestIndex, 1, 2, 1, 0, 50, 5);
        _questStore.UpdateRally(settlementPid, QuestIndex, 0, 0, 0, 0, 50, 5);
        var afterSettlement = _questStore.GetProgress(settlementPid, QuestIndex);
        _questStore.UpdateRally(settlementPid, QuestIndex, 2, 2, 1, 0, 20, 2);
        _questStore.UpdateRally(settlementPid, QuestIndex, 1, 1, 2, 0, 0, 0);
        var afterMarker = _questStore.GetProgress(settlementPid, QuestIndex);
        _questStore.UpdateRally(settlementPid, QuestIndex, 0, 0, 0, 0, 20, 2);
        var afterLateSettlement = _questStore.GetProgress(settlementPid, QuestIndex);
        Check(afterSettlement is { BP: 0, QT: 0, Complete: 1, RallyPoint: 1 }
            && afterMarker is { BP: 20, QT: 2, Complete: 2 }
            && afterLateSettlement is { BP: 0, Complete: 2 }
            && QuestStore.IsSettlementReport(0, 0, 0) && !QuestStore.IsSettlementReport(1, 2, 0),
            "journal settlements consume the running BP without lowering completion; zero markers keep it");
        _questStore.UpdateRally(settlementPid, QuestIndex, 1, 2, 1, 0, 40, 4);
        var hostLossTotals = _questStore.TakeRunTotals(settlementPid, QuestIndex);
        Check(hostLossTotals == (40, 4) && _questStore.GetProgress(settlementPid, QuestIndex) is { BP: 0, QT: 0 }
            && _questStore.TakeRunTotals(settlementPid, QuestIndex) == (0, 0),
            "a lost host's run BP and QT are handed over once for crediting, as a settlement would");
    }
}
