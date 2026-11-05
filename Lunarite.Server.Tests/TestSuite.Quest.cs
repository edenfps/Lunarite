using System.Buffers.Binary;

using Lunarite.Handlers;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void FirstMissionVariants()
    {
        _questStore.Reset(_recommendedLevelCharacter.Pid);
        var recommendedLevelAvailable = _questConditions.Available(_recommendedLevelCharacter.Pid);
        Check(recommendedLevelAvailable.Contains(846105)
            && !recommendedLevelAvailable.Contains(823013)
            && _questCatalog.Get(846105) is { RecommendLevel: 32 }
            && _questCatalog.Get(823013) is { RecommendLevel: 30 },
            "recommendation-only free missions stay available while explicit retail level conditions remain authoritative");
        var chainCoverage = _questConditions.AuditChains();
        Check(chainCoverage.Rules == 530
            && chainCoverage.MissingCatalogEntries.Count == 0
            && chainCoverage.MissingPrerequisites.Count == 0
            && chainCoverage.Cycles.Count == 0,
            "all 530 exported quest gates have catalog entries and an acyclic, fully resolved prerequisite graph");
        var reachabilityCoverage = _questConditions.AuditReachability();
        Check(reachabilityCoverage.Rules == 530
            && reachabilityCoverage.UnreachableQuests.Count == 0,
            "all 530 exported quest gates are satisfiable by a legal level/style state using same-faction prerequisites");
        foreach (var (species, style, expected, siblings) in new[]
        {
            (Species: (byte)1, Style: (byte)1, Expected: 820100, Siblings: new[] { 820101, 820102 }),
            (Species: (byte)1, Style: (byte)2, Expected: 820101, Siblings: new[] { 820100, 820102 }),
            (Species: (byte)1, Style: (byte)3, Expected: 820102, Siblings: new[] { 820100, 820101 }),
            (Species: (byte)11, Style: (byte)1, Expected: 870100, Siblings: new[] { 870101, 870102 }),
            (Species: (byte)11, Style: (byte)2, Expected: 870101, Siblings: new[] { 870100, 870102 }),
            (Species: (byte)11, Style: (byte)3, Expected: 870102, Siblings: new[] { 870100, 870101 })
        })
        {
            var gateCharacter = _characterStore.Create(930000 + expected, $"Gate{expected}", species, new Custom());
            _characterStore.SetStyle(gateCharacter.Pid, style);
            ResetCharacter(gateCharacter.Pid);
            _questStore.Reset(gateCharacter.Pid);
            var available = _questConditions.Available(gateCharacter.Pid);
            Check(available.Contains(expected) && !siblings.Any(available.Contains),
                $"species {species} style {style} receives only its retail first-mission variant {expected}");
        }
    }

    private void CityKismetQuests()
    {
        var cityObjectiveWire = new byte[12];
        cityObjectiveWire[0] = 55;
        BinaryPrimitives.WriteInt32LittleEndian(cityObjectiveWire.AsSpan(1), _cityQuestWirePid);
        BinaryPrimitives.WriteInt32LittleEndian(cityObjectiveWire.AsSpan(5), AcquiredCityQuest);
        cityObjectiveWire[9] = 0;
        cityObjectiveWire[10] = 1;
        cityObjectiveWire[11] = 1;
        Game.OnHandlePacket(_questSocketClient, cityObjectiveWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer
            {
                Opcode = 58, Answer = 0, QuestIndex = AcquiredCityQuest, Index = 0, Count = 1, State = 1
            }.Serialize()) && _questStore.GetProgress(_cityQuestWirePid, AcquiredCityQuest)?.Complete == 2,
            "acquired city Kismet op55 receives success and enables quest reward without a mission room");
        BinaryPrimitives.WriteInt32LittleEndian(cityObjectiveWire.AsSpan(1), OfflineGuestPid);
        Game.OnHandlePacket(_questSocketClient, cityObjectiveWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer
            {
                Opcode = 58, Answer = 1, QuestIndex = AcquiredCityQuest, Index = 0, Count = 1, State = 1
            }.Serialize()), "city objective wire path rejects a different character PID");
        BinaryPrimitives.WriteInt32LittleEndian(cityObjectiveWire.AsSpan(1), _cityQuestWirePid);
        BinaryPrimitives.WriteInt32LittleEndian(cityObjectiveWire.AsSpan(5), 822000);
        Game.OnHandlePacket(_questSocketClient, cityObjectiveWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer
            {
                Opcode = 58, Answer = 1, QuestIndex = 822000, Index = 0, Count = 1, State = 1
            }.Serialize()), "city objective wire path rejects an unacquired quest");
        const int noRoomFreeQuest = 821604;
        Check(_questCatalog.Get(noRoomFreeQuest) is { QuestType: "QuestType_FreeQuest", UseQuestRoom: false }
            && _questStore.Acquire(_cityQuestWirePid, noRoomFreeQuest),
            "exported no-room FreeQuest can be acquired before its city-side objective callback");
        BinaryPrimitives.WriteInt32LittleEndian(cityObjectiveWire.AsSpan(5), noRoomFreeQuest);
        cityObjectiveWire[10] = 0;
        cityObjectiveWire[11] = 0;
        Game.OnHandlePacket(_questSocketClient, cityObjectiveWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer
            {
                Opcode = 58, Answer = 0, QuestIndex = noRoomFreeQuest, Index = 0, Count = 0, State = 0
            }.Serialize()), "acquired no-room FreeQuest accepts its initial invisible objective report");
        cityObjectiveWire[10] = 3;
        cityObjectiveWire[11] = 1;
        Game.OnHandlePacket(_questSocketClient, cityObjectiveWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer
            {
                Opcode = 58, Answer = 0, QuestIndex = noRoomFreeQuest, Index = 0, Count = 3, State = 1
            }.Serialize()) && _questStore.GetProgress(_cityQuestWirePid, noRoomFreeQuest)?.Complete == 2,
            "no-room FreeQuest objective completion enables its reward through encrypted op55");
        cityObjectiveWire[10] = 0;
        Game.OnHandlePacket(_questSocketClient, cityObjectiveWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer
            {
                Opcode = 58, Answer = 0, QuestIndex = noRoomFreeQuest, Index = 0, Count = 0, State = 1
            }.Serialize()) && _questStore.GetProgress(_cityQuestWirePid, noRoomFreeQuest)?.Complete == 1,
            "a lower absolute no-room FreeQuest objective count revokes reward readiness");
        var cityCompletionWire = new byte[6];
        cityCompletionWire[0] = 45;
        BinaryPrimitives.WriteInt32LittleEndian(cityCompletionWire.AsSpan(1), AcquiredCityQuest);
        cityCompletionWire[5] = byte.MaxValue;
        Game.OnHandlePacket(_questSocketClient, cityCompletionWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestComplete
            {
                Answer = 0, QuestIndex = AcquiredCityQuest
            }.Serialize()) && _questStore.Completed(_cityQuestWirePid).Contains(AcquiredCityQuest)
            && _playerStore.GetProgress(_cityQuestWirePid).BP - _cityBpBefore == 100,
            "Sapiens city Kismet completion settles its exported 100-BP reward over the game gateway");
        const int alternativesCityQuest = 871900;
        var alternativesCityCharacter = _characterStore.Create(930871900, "AltCityQuestWire", 11, new Custom());
        int alternativesCityPid = alternativesCityCharacter.Pid;
        _questSocketClient.Player = _playerManager.Get(alternativesCityPid);
        long alternativesBpBefore = _playerStore.GetProgress(alternativesCityPid).BP;
        var alternativesReward = _questRewards.Get(alternativesCityPid, alternativesCityQuest);
        Check(_questCatalog.Get(alternativesCityQuest) is { UseQuestRoom: false }
            && alternativesReward.BP > 0
            && _questStore.Acquire(alternativesCityPid, alternativesCityQuest),
            "Alternatives city Kismet quest has a faction-correct cooked route and reward");
        BinaryPrimitives.WriteInt32LittleEndian(cityObjectiveWire.AsSpan(1), alternativesCityPid);
        BinaryPrimitives.WriteInt32LittleEndian(cityObjectiveWire.AsSpan(5), alternativesCityQuest);
        cityObjectiveWire[10] = 1;
        cityObjectiveWire[11] = 1;
        Game.OnHandlePacket(_questSocketClient, cityObjectiveWire);
        var alternativesProgressWire = ReadQuestSocketPacket();
        for (int pending = 0; pending < 20 && alternativesProgressWire[0] != 58; pending++)
            alternativesProgressWire = ReadQuestSocketPacket();
        var alternativesProgressState = _questStore.GetProgress(alternativesCityPid, alternativesCityQuest)?.Complete;
        Check(alternativesProgressWire.SequenceEqual(new QuestProgressAnswer
            {
                Opcode = 58, Answer = 0, QuestIndex = alternativesCityQuest, Index = 0, Count = 1, State = 1
            }.Serialize()) && alternativesProgressState == 2,
            $"Alternatives city Kismet objective advances over the authenticated game gateway (wire={Convert.ToHexString(alternativesProgressWire)}, state={alternativesProgressState})");
        BinaryPrimitives.WriteInt32LittleEndian(cityCompletionWire.AsSpan(1), alternativesCityQuest);
        Game.OnHandlePacket(_questSocketClient, cityCompletionWire);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestComplete
            {
                Answer = 0, QuestIndex = alternativesCityQuest
            }.Serialize()) && _questStore.Completed(alternativesCityPid).Contains(alternativesCityQuest)
            && _playerStore.GetProgress(alternativesCityPid).BP - alternativesBpBefore == alternativesReward.BP,
            "Alternatives city Kismet completion settles its cooked BP reward once");
        _questSocketClient.Player = _playerManager.Get(_cityQuestWirePid);
        _questRoomMembers.ClearQuest(OfflineGuestClientId);
        _questRoomMembers.TryClearInstance(OfflineGuestClientId, out _);
        _playerManager.Get(_cityQuestWirePid).CityMap = 0;
        Check(_playerStore.TryAddQuestItems(_cityQuestWirePid, [(613994, 2), (613995, 1)], out _),
            "Nostalonia script certificate and train ticket can enter the test inventory");
        byte[] certificateCells = _playerStore.GetInventory(_cityQuestWirePid, () => [])
            .Where(item => item.InventoryIndex == 0 && item.ItemId == 613994)
            .Select(item => item.InventoryCell).Order().ToArray();
        byte ticketCell = _playerStore.GetInventory(_cityQuestWirePid, () => [])
            .Single(item => item.InventoryIndex == 0 && item.ItemId == 613995).InventoryCell;
        var cityItemReductionWire = new byte[9];
        cityItemReductionWire[0] = 50;
        BinaryPrimitives.WriteInt32LittleEndian(cityItemReductionWire.AsSpan(1), 613994);
        BinaryPrimitives.WriteInt32LittleEndian(cityItemReductionWire.AsSpan(5), 0);
        Game.OnHandlePacket(_questSocketClient, cityItemReductionWire);
        Check(_playerStore.GetItems(_cityQuestWirePid).All(item => item.item != 613994)
            && certificateCells.All(cell => ReadCityItemRemoval().SequenceEqual(new RemoveItem
                { InventoryIndex = 0, Cell = cell }.Serialize())),
            "city Kismet op50 quantity zero removes all certificates and updates the client slot");
        BinaryPrimitives.WriteInt32LittleEndian(cityItemReductionWire.AsSpan(1), 613995);
        BinaryPrimitives.WriteInt32LittleEndian(cityItemReductionWire.AsSpan(5), 1);
        Game.OnHandlePacket(_questSocketClient, cityItemReductionWire);
        Check(_playerStore.GetItems(_cityQuestWirePid).All(item => item.item != 613995)
            && ReadCityItemRemoval().SequenceEqual(new RemoveItem
                { InventoryIndex = 0, Cell = ticketCell }.Serialize()),
            "city Kismet op50 removes the train ticket and updates the client slot");
        // Exercise the vendor's actual encrypted Game route as well as the store helper.
        // The authoritative Sapiens sell rate is 0.5, so the displayed undo price and
        // the later debit must both use 64 CR rather than the item's raw 128 CR.
        ResetCharacter(_ownedA.Pid);
        _playerStore.GetProgress(_ownedA.Pid).Money = 1_000;
        _playerStore.GetInventory(_ownedA.Pid, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = 0, InventoryCell = 5, ItemId = 604221, Count = 2, Durability = 100 });
        Check(_economyRates.TrySetAuthoritative(_testRates),
            "encrypted vendor regression installs the authoritative faction rates");
    }

    private void QuestMetadataAndCityTravel()
    {
        Check(_questCatalog.All.Count(x => x.QuestType == "QuestType_City") == 85
            && _questCatalog.Get(820202)?.QuestType == "QuestType_City",
            "omitted Unreal quest type fields inherit the QuestType_City class default");
        Check(_questManager.IsCityObjectiveReportAuthorized(1, 1, 821900, true)
            && _questManager.IsCityObjectiveReportAuthorized(1, 1, 821604, true)
            && _questManager.IsCityObjectiveReportAuthorized(1, 1, 870705, true)
            && !_questManager.IsCityObjectiveReportAuthorized(1, 2, 821604, true)
            && !_questManager.IsCityObjectiveReportAuthorized(1, 2, 821900, true)
            && !_questManager.IsCityObjectiveReportAuthorized(1, 1, 821900, false)
            && !_questManager.IsCityObjectiveReportAuthorized(1, 1, 820300, true)
            && !_questManager.IsCityObjectiveReportAuthorized(0, 0, 821900, true),
            "city objective reports require an acquired non-room quest on the selected PID");
        var noRoomFreeQuests = _questCatalog.All
            .Where(q => q.QuestType == "QuestType_FreeQuest" && !q.UseQuestRoom).ToArray();
        var directFieldQuests = _questCatalog.All.Where(q => q.QuestType == "QuestType_Free").ToArray();
        Check(directFieldQuests.Length > 0 && directFieldQuests.All(q => q.Objectives.Count == 0),
            "cooked direct-entry field quests have no op55 objective definitions; op54 carries their run result");
        Check(noRoomFreeQuests.Any(q => q.EntryIndex is >= 820000 and < 870000)
            && noRoomFreeQuests.Any(q => q.EntryIndex is >= 870000 and < 900000)
            && noRoomFreeQuests.All(q => _questManager.IsCityObjectiveReportAuthorized(1, 1, q.EntryIndex, true))
            && _questCatalog.All.Where(q => q.UseQuestRoom)
                .All(q => !_questManager.IsCityObjectiveReportAuthorized(1, 1, q.EntryIndex, true)),
            "both factions' exported no-room FreeQuests accept selected-player objective reports without opening room quest authority");
        Check(new[] { 0, 9, 10, 11, 12, 13, 14, 15, 16, 100, 110, 111, 112, 113, 114, 115, 116 }
                .All(zone => ItemManager.IsCityScriptItemReductionAuthorized(1, zone, 0, 613994, 1)
                    && ItemManager.IsCityScriptItemReductionAuthorized(1, zone, 0, 613995, 0))
            && !ItemManager.IsCityScriptItemReductionAuthorized(1, 1, 0, 613994, 1)
            && !ItemManager.IsCityScriptItemReductionAuthorized(1, 101, 0, 613994, 1)
            && !ItemManager.IsCityScriptItemReductionAuthorized(1, 0, 9, 613994, 1)
            && !ItemManager.IsCityScriptItemReductionAuthorized(1, 0, 0, 111201, 1)
            && !ItemManager.IsCityScriptItemReductionAuthorized(0, 0, 0, 613994, 1),
            "city-script deletion covers only both factions' cooked ticket/certificate zones and items");
        Check(_cityZoneCatalog.Get(0)?.Level == "City-SP-Nostalonia_A"
            && _cityZoneCatalog.Get(100)?.Level == "City-AT-Eska_A"
            && new[] { 9, 10, 11, 12, 13, 14, 15, 16 }.All(zone =>
                _cityZoneCatalog.Get(zone)?.Level.StartsWith("City-SP-TrCenter_", StringComparison.Ordinal) == true)
            && new[] { 110, 111, 112, 113, 114, 115, 116 }.All(zone =>
                _cityZoneCatalog.Get(zone)?.Level.StartsWith("City-AT-TrCenter_", StringComparison.Ordinal) == true),
            "every allowed scripted-item zone resolves to its corresponding cooked city-map family");
        Check(_questCatalog.All.Count(x => x.QuestSubType == "QuestSub_Cooperative") == 95
            && _questCatalog.All.Count(x => x.QuestSubType == "QuestSub_Single") == 435
            && _questCatalog.Get(820202)?.QuestSubType == "QuestSub_Single",
            "quest metadata preserves 95 explicit cooperative subtypes and 435 default solo subtypes");
        Check(QuestInstanceManager.IsQuestStartAuthorized(820102, 820102, 17)
            && !QuestInstanceManager.IsQuestStartAuthorized(820102, 820100, 17)
            && !QuestInstanceManager.IsQuestStartAuthorized(820102, 820102, 0),
            "quest start requires the connection's active quest and live instance token");
        Check(QuestInstanceManager.ResolveQuestCreateAcknowledgment(846000, 1, 1, 846000) == 846000
            && QuestInstanceManager.ResolveQuestCreateAcknowledgment(2, 1, 2, 846000) == 846000
            && QuestInstanceManager.ResolveQuestCreateAcknowledgment(3, 1, 2, 846000) == 0
            && QuestInstanceManager.ResolveQuestCreateAcknowledgment(2, 0, 2, 846000) == 0,
            "quest room acknowledgment accepts the host room identity and joining member PID only");
        Check(CityManager.DecodeMapByte(255) == -1 && CityManager.DecodeMapByte(110) == 110,
            "city travel sign-extends the native byte INDEX_NONE sentinel");
        Check(_cityZoneCatalog.Get(0) is { ZoneName: "Nostalonia_A", Level: "City-SP-Nostalonia_A" }
            && _cityZoneCatalog.Get(110) is { ZoneName: "City-AT-TrCenter_1" }
            && !_cityZoneCatalog.IsKnown(-1) && !_cityZoneCatalog.IsKnown(77),
            "exported city-zone catalog preserves the omitted zero index and rejects sentinels/nonexistent maps");
        Check(_cityZoneCatalog.IsAvailableToCharacter(0, 1)
            && _cityZoneCatalog.IsAvailableToCharacter(20, 6)
            && !_cityZoneCatalog.IsAvailableToCharacter(100, 1)
            && _cityZoneCatalog.IsAvailableToCharacter(100, 11)
            && _cityZoneCatalog.IsAvailableToCharacter(119, 16)
            && !_cityZoneCatalog.IsAvailableToCharacter(19, 16),
            "city-zone authority follows the cooked faction-specific table selected by character tribe");
        Check(_cityZoneCatalog.Get(0) is { Areas.Count: 41 }
            && _cityZoneCatalog.Get(0)!.Areas[0] == "NOS_GT_01"
            && _cityZoneCatalog.Get(100) is { Areas.Count: 42 }
            && _cityZoneCatalog.Get(100)!.Areas[41] == "ESKA_HQ_04"
            && _cityZoneCatalog.IsValidArea(0, 40, 1)
            && !_cityZoneCatalog.IsValidArea(0, 41, 1)
            && _cityZoneCatalog.IsValidArea(100, 41, 11)
            && !_cityZoneCatalog.IsValidArea(100, 42, 11)
            && !_cityZoneCatalog.IsValidArea(100, 0, 1),
            "city area indices preserve the cooked ordered CityArea arrays and faction boundary");
        Check(!_cityZoneCatalog.IsValidArea(98, 0, 1, _ => false)
            && !_cityZoneCatalog.IsValidArea(99, 0, 1, _ => false)
            && _cityZoneCatalog.IsValidArea(98, 0, 1, _ => true)
            && _cityZoneCatalog.IsValidArea(10, 0, 1, _ => true)
            && _cityZoneCatalog.IsValidArea(7, 0, 1, _ => false),
            "city travel gates unavailable building packages without blocking special quest rooms");
        Check(_p2pManager.RetainedCityDestination(0, (0, 40), 1) == (0, 40)
            && _p2pManager.RetainedCityDestination(0, (110, 0), 1) == (0, 0)
            && _p2pManager.RetainedCityDestination(100, (100, 41), 11) == (100, 41)
            && _p2pManager.RetainedCityDestination(100, (100, 41), 1) == (10, 0),
            "invalid city travel retains the current valid area instead of relocating to area zero");
        Check(_characterManager.TutorialStart(1) == (8, 810101) && _characterManager.TutorialStart(6) == (8, 810101)
            && _characterManager.TutorialStart(11) == (108, 860101) && _characterManager.TutorialStart(16) == (108, 860101),
            "each faction's tutorial is the Zone_TutorialQuest zone whose Level is its QuestType_Tutorial quest");
        Check(CharacterManager.ShouldStartTutorial(null, false, false) && CharacterManager.ShouldStartTutorial(10, true, false)
            && !CharacterManager.ShouldStartTutorial(10, false, false) && !CharacterManager.ShouldStartTutorial(null, true, true),
            "only never-placed or mid-tutorial characters that have not completed the tutorial start it");
        var tutorialKit = ItemManager.TutorialLoadoutItems();
        Check(tutorialKit.Where(i => i.InvenIndex == 3).OrderBy(i => i.Cell).Select(i => i.ItemID)
                .SequenceEqual(new[] { 100499, 100299, 100899 })
            && tutorialKit.Where(i => i.InvenIndex == 4).Select(i => i.ItemID).Order()
                .SequenceEqual(new[] { 200199, 200299, 200399, 200499 })
            && tutorialKit.Single(i => i.ItemID == 200499).Skill0 == 303400
            && tutorialKit.All(i => i.Durability == 100),
            "tutorial missions carry the cooked Tutorial kit: Machine Gun, Flinger, Regulator, tutorial armor + Sprint");
        Check(_questManager.IsQuestLevelZone(8) && _questManager.IsQuestLevelZone(108) && !_questManager.IsQuestLevelZone(18) && !_questManager.IsQuestLevelZone(10),
            "tutorial quest zones are never loaded as city maps; the Simulation Center zone still is");
        Check(_cityManager.ResolveMoveMapReturn((0, 40), 1) == (0, 40)
            && _cityManager.ResolveMoveMapReturn((100, 41), 11) == (100, 41)
            && _cityManager.ResolveMoveMapReturn(null, 1) == (10, 0)
            && _cityManager.ResolveMoveMapReturn((100, 41), 1) == (10, 0)
            && _cityManager.ResolveMoveMapReturn((-1, 0), 11) == (110, 0),
            "MoveMapReturn goes back to the zone the player came from, else the faction training center");
        Check(_cityManager.IsReportedCityAreaValid(0, 40, 1)
            && !_cityManager.IsReportedCityAreaValid(0, 41, 1)
            && _cityManager.IsReportedCityAreaValid(100, 41, 11)
            && !_cityManager.IsReportedCityAreaValid(100, byte.MaxValue, 11)
            && !_cityManager.IsReportedCityAreaValid(100, 0, 1),
            "city position reports persist only areas belonging to the current faction zone");
        Check(new ServerTime { TickMilliseconds = 1234, UnixSeconds = 5678 }.Serialize()
                .SequenceEqual(new byte[] { 2, 210, 4, 0, 0, 46, 22, 0, 0 }),
            "server time serializes native opcode 2 dd layout");
        Check(new ServerVersion { Version = 281 }.Serialize()
                .SequenceEqual(new byte[] { 207, 25, 1, 0, 0 }),
            "server version notice serializes native opcode 207 d layout");
        Check(new UpdateItemNum { Inventory = 0, Cell = 7, Count = 12 }.Serialize()
                .SequenceEqual(new byte[] { 83, 0, 7, 12, 0, 0, 0 })
            && new UpdateItemIndex { Inventory = 4, Cell = 2, ItemId = 221001 }.Serialize()
                .SequenceEqual(new byte[] { 84, 4, 2, 73, 95, 3, 0 }),
            "inventory count/index deltas match native op83/op84 bbd layouts");
        Check(new UpdatedItemState { Inventory = 0, Cell = 4, State = 0x20 }.Serialize()
                .SequenceEqual(new byte[] { 214, 0, 4, 0x20 }),
            "bind-state mutation uses native op214 inventory/cell/state layout");
        Check(new UpdateInventoryViews { View = UpdateInventoryViews.Inventory }.Serialize()
                .SequenceEqual(new byte[] { 90, 0 })
            && new UpdateInventoryViews { View = UpdateInventoryViews.Equipment }.Serialize()
                .SequenceEqual(new byte[] { 90, 1 }),
            "inventory/equipment view refresh matches native op90 b layout");
        Check(new SquadMoveInput { Pid = 7, IsLeader = 1, X = 1.5f, Y = -2.25f }.Serialize()
                .SequenceEqual(new byte[] { 108, 7, 0, 0, 0, 1, 0, 0, 192, 63, 0, 0, 16, 192 })
            && new SquadMoveMap { Pid = 7, MapIndex = 10 }.Serialize()
                .SequenceEqual(new byte[] { 109, 7, 0, 0, 0, 10 }),
            "squad HUD movement/map events match native op108 dbff and op109 db layouts");
        Check(new MoveInputForce { X = 1.5f, Y = -2.25f, Z = 3.75f, Yaw = 321 }.Serialize()
                .SequenceEqual(new byte[] { 23, 0, 0, 192, 63, 0, 0, 16, 192,
                    0, 0, 112, 64, 65, 1 }),
            "authoritative movement correction matches native op23 fffw layout");
        Check(new DelPlayer { Pid = 7 }.Serialize().SequenceEqual(new byte[] { 21, 7, 0, 0, 0 })
            && new MoveInput { Pid = 7, X = 1, Y = 2, Z = 3, Yaw = 4 }.Serialize()
                .SequenceEqual(new byte[] { 22, 7, 0, 0, 0, 0, 0, 128, 63, 0, 0, 0, 64,
                    0, 0, 64, 64, 4, 0 })
            && new MoveEvent { Pid = 7, Event = 1 }.Serialize()
                .SequenceEqual(new byte[] { 24, 7, 0, 0, 0, 1 })
            && new Animation { Pid = 7, Name = "Jump" }.Serialize()
                .SequenceEqual(new byte[] { 26, 7, 0, 0, 0, 74, 117, 109, 112, 0 })
            && new EquipArmorOther { Pid = 7, Part = 2, ItemId = 221201 }.Serialize()
                .SequenceEqual(new byte[] { 68, 7, 0, 0, 0, 2, 17, 96, 3, 0 }),
            "presence leave, movement, event, and animation packets match native layouts");
        var noClanHeadline = _clanManager.BuildClanHeadline(920_777).Serialize();
        Check(noClanHeadline.SequenceEqual(new byte[] { 215, 201, 12, 14, 0, 0, 0 }),
            "peer snapshots include the native empty-clan headline and authoritative grade");
        var armorDataBytes = new UpdateArmorOther
        {
            Pid = 7, Part = 2, Item = new PlayerStore.GenericItem
            { ItemId = 221201, Count = 1, InventoryIndex = 4, InventoryCell = 2, Skill = 303400, Durability = 91 }
        }.Serialize();
        Check(armorDataBytes.Length == 38 && armorDataBytes[0] == 80 && armorDataBytes[5] == 2
            && armorDataBytes.AsSpan(6, 4).SequenceEqual(new byte[] { 17, 96, 3, 0 })
            && armorDataBytes.AsSpan(34, 4).SequenceEqual(new byte[] { 91, 0, 0, 0 }),
            "other-player armor-data update matches native op80 dbM layout");
        var questItemUpdate = new QuestUpdateItem
        {
            Pid = 7,
            Item = new PlayerStore.GenericItem { ItemId = 221001, InventoryIndex = 4, InventoryCell = 2, Durability = 91 }
        }.Serialize();
        Check(questItemUpdate.Length == 37 && questItemUpdate[0] == 120
            && questItemUpdate.AsSpan(1, 4).SequenceEqual(new byte[] { 7, 0, 0, 0 })
            && questItemUpdate.AsSpan(5, 4).SequenceEqual(new byte[] { 73, 95, 3, 0 })
            && questItemUpdate.AsSpan(33, 4).SequenceEqual(new byte[] { 91, 0, 0, 0 })
            && new QuestSubItem { Pid = 7, Inventory = 4, Cell = 2 }.Serialize()
                .SequenceEqual(new byte[] { 121, 7, 0, 0, 0, 4, 2 }),
            "quest party item update/removal match native op120 dm32 and op121 dbb layouts");
        _addPlayerBytes = new AddPlayer
        {
            Pid = 7, Name = "A", Index = 6, X = 1, Y = 2, Z = 3, Yaw = 4,
            Style = 2, Level = 9, VehicleIndex = 0, ClanName = "C",
            EquippedItems = [new PlayerStore.GenericItem { ItemId = 221001, Count = 1, InventoryIndex = 4,
                InventoryCell = 0, Durability = 100 }]
        }.Serialize();
    }

    private void MissionRunRecords()
    {
        var questBlock = new byte[48];
        questBlock[5] = 11; // EBZType_Quest
        BinaryPrimitives.WriteInt32LittleEndian(questBlock.AsSpan(10), 5);
        BinaryPrimitives.WriteInt32LittleEndian(questBlock.AsSpan(14), 1);
        BinaryPrimitives.WriteInt32LittleEndian(questBlock.AsSpan(18), 3);
        var questDetail = new byte[104];
        BinaryPrimitives.WriteInt32LittleEndian(questDetail.AsSpan(13 * 4), 1); // KILLSPREE_MASSMURDER
        _battleResultStore.SetQuestRun(QuestRecordPid, "q:1", _weaponBlock, questBlock, questDetail, 300);
        BinaryPrimitives.WriteInt32LittleEndian(questBlock.AsSpan(10), 9);
        _battleResultStore.SetQuestRun(QuestRecordPid, "q:1", _weaponBlock, questBlock, questDetail, 420);
        _battleResultStore.Add(new BattleResultRequest(QuestRecordPid, 0, 9, new byte[27], _weaponBlock, _battleBlock, new byte[104]));
        var questSummary = _battleResultStore.BuildRecordSheet(QuestRecordPid, 1);
        Check(_battleResultStore.ForPid(QuestRecordPid).Count == 2
            && BinaryPrimitives.ReadInt32LittleEndian(questSummary.AsSpan(9 * 4)) == 420
            && BinaryPrimitives.ReadInt32LittleEndian(questSummary.AsSpan(11 * 4)) == 9
            && BinaryPrimitives.ReadInt32LittleEndian(questSummary.AsSpan(12 * 4)) == 3
            && BinaryPrimitives.ReadInt32LittleEndian(questSummary.AsSpan(16 * 4)) == 1
            && BinaryPrimitives.ReadInt32LittleEndian(questSummary.AsSpan(14 * 4)) == 7
            && BinaryPrimitives.ReadInt32LittleEndian(questSummary.AsSpan(17 * 4)) == 1
            && _battleResultStore.TotalKillsDeaths(QuestRecordPid) == (7, 2),
            "op60 mission runs keep their latest report and fill quest time, enemy kills, deaths, assists and sprees, not PvP totals");
        _battleResultStore.ResetRecord(QuestRecordPid);
    }

    private void FieldObjectiveProgress()
    {
        Check(_questStore.BeginDirectFieldRun(DirectFieldTelemetryPid, _directFieldQuest)
            && _questStore.GetProgress(DirectFieldTelemetryPid, _directFieldQuest).KilledMonsters == 0
            && _questStore.GetProgress(DirectFieldTelemetryPid, _directFieldQuest).PlayTime == 0
            && _questStore.GetProgress(DirectFieldTelemetryPid, _directFieldQuest).WeaponKills.Count == 0
            && _questStore.UpdateWeaponKills(DirectFieldTelemetryPid, _directFieldQuest,
                new (byte Category, ushort Kills)[] { (1, 2) }, authenticatedDirectFieldRun: true)
            && _questStore.GetProgress(DirectFieldTelemetryPid, _directFieldQuest).WeaponKills[(byte)1] == 2
            && !_questStore.BeginDirectFieldRun(DirectFieldTelemetryPid, QuestIndex),
            "new direct-entry field instance resets prior run telemetry without touching non-field quests");
        const int acquiredFieldTelemetryPid = 920302;
        _questStore.Reset(acquiredFieldTelemetryPid);
        Check(_questStore.Acquire(acquiredFieldTelemetryPid, _directFieldQuest)
            && _questStore.UpdateWeaponKills(acquiredFieldTelemetryPid, _directFieldQuest,
                new (byte Category, ushort Kills)[] { (1, 3) })
            && !_questStore.BeginDirectFieldRun(acquiredFieldTelemetryPid, _directFieldQuest)
            && _questStore.GetProgress(acquiredFieldTelemetryPid, _directFieldQuest).WeaponKills[(byte)1] == 3,
            "direct-field run reset preserves an explicitly acquired quest's journal progress");
        Check(_questProgress.Objectives[1].Count == 7 && _questProgress.Objectives[1].Visible,
            "quest objective state round-trips");
        Check(_questStore.Acquire(FieldEventPid, 871604)
            && _questStore.RecordFieldEvent(FieldEventPid, "FQOType_KillEnemy", 9111621).Single()
                is { QuestIndex: 871604, ObjectiveIndex: 0, Count: 1 }
            && _questStore.RecordFieldEvent(FieldEventPid, "FQOType_KillEnemy", 9111621, 9).Single().Count == 3
            && _questStore.ReadyToComplete(FieldEventPid, 871604),
            "exact enemy events advance and cap matching exported field objectives");
    }
}
