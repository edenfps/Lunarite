using System.Buffers.Binary;
using System.Net;

using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void StarterArmorAndSimulationStart()
    {
        Check(ItemManager.TutorialArmorItemIds().SequenceEqual(new[] { 200199, 200299, 200399, 200499 }),
            "character creation uses the exact extracted Tutorial initial-armor group");
        var initialArmorPreview = ItemManager.InitialArmorPreview();
        Check(initialArmorPreview.Length == 5
            && initialArmorPreview.Select(x => x.ItemID).SequenceEqual(new[] { 0, 200199, 200299, 200399, 200499 })
            && initialArmorPreview.Select(x => x.InventoryCell).SequenceEqual(new byte[] { 0, 1, 2, 3, 4 })
            && initialArmorPreview.Select(x => x.General3).SequenceEqual(new[] { 0, 100, 100, 100, 100 }),
            "character-create response emits the native five armor records with an empty helmet slot");
        var createResponse = new CreatePlayer
        {
            Answer = 0,
            Pid = 1,
            Name = "A",
            Custom = new Custom(),
            ArmorList = initialArmorPreview
        }.Serialize();
        Check(createResponse.Length >= 60
            && BitConverter.ToInt32(createResponse, createResponse.Length - 60 + 4) == 0
            && createResponse[createResponse.Length - 48] == 1
            && BitConverter.ToInt32(createResponse, createResponse.Length - 48 + 4) == 200199
            && BitConverter.ToInt32(createResponse, createResponse.Length - 48 + 8) == 100
            && createResponse[createResponse.Length - 12] == 4
            && BitConverter.ToInt32(createResponse, createResponse.Length - 12 + 4) == 200499
            && BitConverter.ToInt32(createResponse, createResponse.Length - 12 + 8) == 100,
            "character-create response ends in the exact native 60-byte armor block");
        var paddedCreateResponse = new CreatePlayer
        {
            Answer = 0, Pid = 1, Name = "A", Custom = new Custom(), ArmorList = []
        }.Serialize();
        Check(paddedCreateResponse.Length == createResponse.Length
            && paddedCreateResponse.AsSpan(paddedCreateResponse.Length - 60).ToArray().All(x => x == 0),
            "character-create serializer always pads the native five-record armor block");
        Check(CharacterStore.SimulationCenterMap(1) == 18 && CharacterStore.SimulationCenterMap(6) == 18
            && CharacterStore.SimulationCenterMap(11) == 118 && CharacterStore.SimulationCenterMap(16) == 118,
            "retail species route to the correct Sapiens or Alternatives simulation-center map");
        Check(DiscordPresence.TryParse(new byte[] { (byte)'L', (byte)'R', (byte)'P', (byte)'1', 0x34, 0x12 }, out ushort presencePort)
            && presencePort == 0x1234 && !DiscordPresence.TryParse(new byte[12], out _),
            "Discord status: request carries the game link's port and differs from a server-list ping");
        Check(DiscordPresence.SameHost(IPAddress.Parse("::ffff:1.2.3.4"), IPAddress.Parse("1.2.3.4"))
            && !DiscordPresence.SameHost(IPAddress.Parse("1.2.3.4"), IPAddress.Parse("1.2.3.5")),
            "Discord status: only the link's own address gets an answer");
        var presenceHome = IPAddress.Parse("5.6.7.8");
        var presenceLinks = new[] { (1, 10, presenceHome, 51000), (2, 11, IPAddress.Parse("9.9.9.9"), 51001), (3, 12, IPAddress.Parse("9.9.9.9"), 51002) };
        Check(DiscordPresence.Choose(presenceLinks, 60000, presenceHome)?.Item1 == 1
            && DiscordPresence.Choose(presenceLinks, 51002, IPAddress.Parse("9.9.9.9"))?.Item1 == 3
            && DiscordPresence.Choose(presenceLinks, 60000, IPAddress.Parse("9.9.9.9")) == null,
            "Discord status: a router-rewritten port still finds a lone character; shared addresses need the port");
        var presenceJson = System.Text.Json.JsonDocument.Parse(DiscordPresence.ActivityJson(new string('x', 200), "state", "Lv 1 Sapiens", 5)).RootElement;
        Check(presenceJson.GetProperty("details").GetString()!.Length == 128 && presenceJson.GetProperty("timestamps").GetProperty("start").GetInt64() == 5
            && presenceJson.GetProperty("assets").GetProperty("large_image").GetString() == "huxley",
            "Discord status: activity fits Discord's limits and uses the uploaded logo");
        Check(SimulationManager.SoloBotReward(1001, 40) == (500, 20),
            "Skirmish: one human against bots earns half the match's BP and QT");
        const int SoloPid = 77_002;
        int soloFirst = _playerStore.TakeSoloBotAllowance(SoloPid, 700, 1000, 20261110);
        int soloSecond = _playerStore.TakeSoloBotAllowance(SoloPid, 700, 1000, 20261110);
        int soloNextDay = _playerStore.TakeSoloBotAllowance(SoloPid, 700, 1000, 20261111);
        Check(soloFirst == 700 && soloSecond == 300 && soloNextDay == 700,
            "Skirmish: bot-only BP stops at the daily limit and starts again the next UTC day");
        Check(BattleInstanceManager.BattlefieldBotOptions(48, "1", null) == "?Mutator=LunariteBots.LunariteBotMutator?BotsPerTeam=8"
            && BattleInstanceManager.BattlefieldBotOptions(48, "1", "30") == "?Mutator=LunariteBots.LunariteBotMutator?BotsPerTeam=24"
            && BattleInstanceManager.BattlefieldBotOptions(48, null, "8") == ""
            && BattleInstanceManager.BattlefieldBotOptions(48, "1", "0") == "",
            "Battlefield bots: off by default, eight a side when on, never more than half the field");
        const int PlayedPid = 77_001;
        long playedBefore = _playerStore.GetProgress(PlayedPid).PlayedSeconds;
        _playerStore.AddPlayedSeconds([PlayedPid, 0, -1], 60);
        _playerStore.AddPlayedSeconds([PlayedPid], 60);
        Check(_playerStore.GetProgress(PlayedPid).PlayedSeconds == playedBefore + 120,
            "play time adds up per character and skips invalid PIDs");
        Check(SimulationManager.SkirmishBotsPerTeam(1, 8, "room", new byte[] { 1 }, true) == 4
            && SimulationManager.SkirmishBotsPerTeam(1, 2, "room", new byte[] { 1 }, true) == 1
            && SimulationManager.SkirmishBotsPerTeam(2, 16, "room", new byte[] { 1, 1, 1, 1, 2, 3 }, true) == 8
            && SimulationManager.SkirmishBotsPerTeam(1, 8, "room", new byte[] { 1 }, false) == 0
            && SimulationManager.SkirmishBotsPerTeam(1, 8, "My NoBots room", new byte[] { 1 }, true) == 0
            && SimulationManager.SkirmishBotsPerTeam(5, 8, "room", new byte[] { 1 }, true) == 4
            && SimulationManager.SkirmishBotsPerTeam(6, 8, "room", new byte[] { 1 }, true) == 0
            && SimulationManager.SkirmishBotOptions(0) == ""
            && SimulationManager.SkirmishBotOptions(4) == "?Mutator=LunariteBots.LunariteBotMutator?BotsPerTeam=4",
            "Skirmish bots: team modes, DM and HM, opt-out by room name, sides filled and capped by the room size");
        Check(SimulationManager.CheckStartCondition(1, 8, new byte[] { 1, 3 })
            && SimulationManager.CheckStartCondition(1, 8, new byte[] { 1, 2, 3 })
            && !SimulationManager.CheckStartCondition(1, 8, new byte[] { 1, 2, 2, 3 })
            && !SimulationManager.CheckStartCondition(1, 8, new byte[] { 1 })
            && !SimulationManager.CheckStartCondition(2, 8, new byte[] { 1, 2, 3 })
            && SimulationManager.CheckStartCondition(2, 8, new byte[] { 1, 2, 3, 4 })
            && SimulationManager.CheckStartCondition(5, 8, new byte[] { 1, 2 })
            && !SimulationManager.CheckStartCondition(5, 8, new byte[] { 1 }),
            "Simulation start follows ActiveGameStartConditions: 2+ players and the per-mode side ratio");
        Check(SimulationManager.IsSimulationSquadBalanced(new byte[] { 0, 1 })
            && SimulationManager.IsSimulationSquadBalanced(new byte[] { 0, 0, 1, 1 })
            && !SimulationManager.IsSimulationSquadBalanced(new byte[] { 0, 0, 1 })
            && !SimulationManager.IsSimulationSquadBalanced(new byte[] { 0, 2 }),
            "Simulation TDM start requires equal nonempty room squads");
        Check(!SimulationManager.IsSimulationGuestAdmissionComplete(0)
            && SimulationManager.IsSimulationGuestAdmissionComplete(1)
            && SimulationManager.IsSimulationGuestAdmissionComplete(2)
            && !SimulationManager.IsSimulationGuestAdmissionComplete(4),
            "Simulation admission completes at native warmup READY or PLAY, never idle or BUSY");
        Check(SimulationManager.IsSimulationEndpointListening(unchecked((int)0x0200007F), 7777,
                new[] { new IPEndPoint(IPAddress.Parse("127.0.0.2"), 7777) })
            && !SimulationManager.IsSimulationEndpointListening(unchecked((int)0x0200007F), 7777,
                new[] { new IPEndPoint(IPAddress.Parse("127.0.0.3"), 7777) }),
            "Simulation warmup join recognizes only the exact packed UE3 host endpoint");
        Check(CharacterManager.GetLevelClass(1) == 0 && CharacterManager.GetLevelClass(9) == 0
            && CharacterManager.GetLevelClass(10) == 1 && CharacterManager.GetLevelClass(19) == 1
            && CharacterManager.GetLevelClass(20) == 2 && CharacterManager.GetLevelClass(29) == 2
            && CharacterManager.GetLevelClass(30) == 3 && CharacterManager.GetLevelClass(255) == 3,
            "Simulation Center level classes match exported UnrealScript C/B/A/S thresholds");
        Check(_accountStore.TryRegister("ownerc", "secret-c", out _accountC), "third account registers for roster-limit test");
    }

    private void SkillEquip()
    {
        Check(_playerStore.TryEquipSkill(SkillPid, 1, 1, 9, 1, out var socketArmor)
            && socketArmor is { Skill1: 303400 }
            && _playerStore.GetInventory(SkillPid, () => []).Any(i => i.InventoryIndex == 6 && i.InventoryCell == 1 && i.ItemId == 303400),
            "skill equip moves the skill item MAIN-to-SKILL and persists armor socket 1");
        var equipSkillWire = new EquipSkill { Result = 0, SkillCell = 1, MainCell = 9, Armor = socketArmor }.Serialize();
        Check(equipSkillWire.Length == 36 && equipSkillWire[0] == 74 && equipSkillWire[2] == 1 && equipSkillWire[3] == 9
            && BinaryPrimitives.ReadInt32LittleEndian(equipSkillWire.AsSpan(28)) == 303400,
            "server op74 serializes skill/main cells then exact updated GenericItem");
        Check(_playerStore.TryTakeOffSkill(SkillPid, 1, 1, 0xFF, 0xFF,
                out byte skillMainCell, out byte skillCell, out var clearedArmor)
            && skillCell == 1 && clearedArmor is { Skill1: 0 }
            && _playerStore.GetInventory(SkillPid, () => []).Any(i => i.InventoryIndex == 0 && i.InventoryCell == skillMainCell && i.ItemId == 303400),
            "skill takeoff resolves free cells, clears the armor socket, and returns the skill to MAIN");
        var takeSkillWire = new TakeOffSkill { Result = 0, SkillCell = skillCell, MainCell = skillMainCell, Armor = clearedArmor }.Serialize();
        Check(takeSkillWire.Length == 36 && takeSkillWire[0] == 75
            && takeSkillWire[^2] == skillCell && takeSkillWire[^1] == skillMainCell,
            "server op75 serializes updated GenericItem before resolved skill/main cells");
        Check(_playerStore.SwapSkillSlots(SkillPid, 3, 4)
            && _playerStore.GetSkillSlots(SkillPid).SequenceEqual(new byte[] { 0, 1, 2, 4, 3 })
            && !_playerStore.SwapSkillSlots(SkillPid, 5, 0)
            && new UpdateSkillSlot { Result = 0, Slot1 = 3, Slot2 = 4 }.Serialize().SequenceEqual(new byte[] { 76, 0, 3, 4 }),
            "skill-slot swaps persist as a validated five-entry permutation and op76 closes the transaction");
        Check(new ListenUpdateSkillSlot { Pid = SkillPid, SkillSlots = _playerStore.GetSkillSlots(SkillPid) }.Serialize()
                .SequenceEqual(new byte[] { 122, 0xC7, 0x09, 0x0E, 0, 0, 1, 2, 4, 3 }),
            "server op122 serializes PID plus the exact five-byte skill-slot permutation");
    }

    private void PeerArmorAndQuestAcquisition()
    {
        Check(_addPlayerBytes.Length == 83
            && _addPlayerBytes[0] == 20
            && _addPlayerBytes.AsSpan(1, 4).SequenceEqual(new byte[] { 7, 0, 0, 0 })
            && _addPlayerBytes.AsSpan(59, 4).SequenceEqual(new byte[] { 9, 0, 0, 0 })
            && _addPlayerBytes.AsSpan(63, 4).SequenceEqual(new byte[] { 0, 0, 0, 0 })
            && _addPlayerBytes[70] == 1
            && _addPlayerBytes[71] == 0
            && _addPlayerBytes.AsSpan(75, 4).SequenceEqual(new byte[] { 73, 95, 3, 0 })
            && _addPlayerBytes.AsSpan(79, 4).SequenceEqual(new byte[] { 100, 0, 0, 0 }),
            "other-player add follows native dsbMmmbbddsb header and compact armor stream");
        const int cityArmorSnapshotPid = 920115;
        ResetCharacter(cityArmorSnapshotPid);
        var cityArmorSnapshot = _characterManager.BuildOtherPlayerArmorSnapshot(cityArmorSnapshotPid);
        Check(cityArmorSnapshot.Count == 4
            && cityArmorSnapshot.Select(x => x.Part).SequenceEqual(new byte[] { 1, 2, 3, 4 })
            && cityArmorSnapshot.All(x => x.Pid == cityArmorSnapshotPid && x.Item is { InventoryIndex: 4 })
            && cityArmorSnapshot.Select(x => x.Serialize()[0]).All(opcode => opcode == 80),
            "city peer creation follows compact op20 preview with complete native op80 armor snapshot");
        var expiredArmorPresence = CityManager.BuildExpiredArmorPresence(7,
            new PlayerStore.ExpiredItem(4, 2, 221201, 0, 0));
        Check(expiredArmorPresence.Count == 2
            && expiredArmorPresence[0].Serialize()
                .SequenceEqual(new byte[] { 68, 7, 0, 0, 0, 2, 0, 0, 0, 0 })
            && expiredArmorPresence[1].Serialize()
                .SequenceEqual(new byte[] { 121, 7, 0, 0, 0, 4, 2 })
            && CityManager.BuildExpiredArmorPresence(7,
                new PlayerStore.ExpiredItem(3, 0, 100100, 0, 0)).Count == 0,
            "expired equipped armor clears the native city and quest peer wrappers");
        Check(VehicleManager.BuildExpiredVehiclePresence(7).Serialize()
                .SequenceEqual(new byte[] { 175, 7, 0, 0, 0, 0, 0, 0, 0 }),
            "expired mounted vehicles publish the native zero-item dismount update");
        Check(new QuestAcquire { Answer = 0, QuestIndex = 820102 }.Serialize()
                .SequenceEqual(new byte[] { 37, 0, 134, 131, 12, 0 }),
            "quest acquisition uses native op37 bd transaction callback");
        Check(new QuestAcquire { Answer = QuestAcquire.AlreadyAcquired, QuestIndex = 820102 }.Serialize()
                .SequenceEqual(new byte[] { 37, 21, 134, 131, 12, 0 })
            && QuestAcquire.NotFound == 3 && QuestAcquire.Impossible == 22
            && QuestAcquire.NotAcquired == 23 && QuestAcquire.WrongObjective == 29
            && QuestAcquire.InventoryFull == 30 && QuestAcquire.AlreadyComplete == 31
            && QuestAcquire.DontSelectItem == 37,
            "quest acquisition preserves native not-found, duplicate, impossible, and completed result values");
        Check(new QuestComplete { Answer = QuestAcquire.InventoryFull, QuestIndex = 820102 }.Serialize()
                .SequenceEqual(new byte[] { 49, 30, 134, 131, 12, 0 })
            && new QuestRetire { Answer = QuestAcquire.NotAcquired, QuestIndex = 820102 }.Serialize()
                .SequenceEqual(new byte[] { 48, 23, 134, 131, 12, 0 }),
            "quest completion and retirement carry exact native inventory-full and not-acquired results");
        Check(new ServerSideQuestPossibleList { Quests = [820100, 820102] }.Serialize()
                .SequenceEqual(new byte[] { 36, 2, 0, 132, 131, 12, 0, 134, 131, 12, 0 }),
            "server-side quest availability push matches native op36 w plus int array layout");
        Check(new QuestListenSpawn { Pid = 7, Answer = 0 }.Serialize()
                .SequenceEqual(new byte[] { 61, 7, 0, 0, 0, 0 }),
            "quest listen-spawn callback matches native op61 db layout");
        Check(QuestManager.IsLocalQuestSpawnRequestAuthorized(1, 820300, 1, 820300)
            && QuestManager.IsLocalQuestSpawnRequestAuthorized(1, 820300, 1, 1)
            && !QuestManager.IsLocalQuestSpawnRequestAuthorized(1, 820300, 0, 820300)
            && !QuestManager.IsLocalQuestSpawnRequestAuthorized(1, 820300, 1, 820301),
            "quest listen spawn accepts the op42 room PID and rejects unrelated identities");
        Check(QuestManager.IsQuestReportIdentityAuthorized(1, 820300, 820300, 1, 820300, true)
            && QuestManager.IsQuestReportIdentityAuthorized(1, 1, 820300, 1, 820300, true)
            && !QuestManager.IsQuestReportIdentityAuthorized(1, 820300, 820300, 1, 820302, false)
            && !QuestManager.IsQuestReportIdentityAuthorized(1, 1, 820300, 0, 820300, true)
            && !QuestManager.IsQuestReportIdentityAuthorized(1, 820301, 820300, 1, 820300, true),
            "quest reports require the active live instance, its exact quest, and an authenticated room identity");
        Check(QuestInstanceManager.ResolveQuestReportPid(1, 2, 820300, 1, 2, 820300, 1, true) == 2
            && QuestInstanceManager.ResolveQuestReportPid(1, 820300, 820300, 1, 0, 0, 0, false) == 1
            && QuestInstanceManager.ResolveQuestReportPid(1, 2, 820300, 1, 2, 820301, 1, true) == 0
            && QuestInstanceManager.ResolveQuestReportPid(1, 2, 820300, 1, 2, 820300, 3, true) == 0
            && QuestInstanceManager.ResolveQuestReportPid(1, 2, 820300, 1, 2, 820300, 1, false) == 0,
            "listen-host reports attribute progress only to an authenticated same-instance guest");
        Check(QuestInstanceManager.ResolveQuestPickupOwner(7, 820300, 7, 7) == 820300
            && QuestInstanceManager.ResolveQuestPickupOwner(8, 820300, 7, 7) == 8
            && QuestInstanceManager.ResolveQuestPickupOwner(0, 820300, 7, 7) == 0,
            "quest pickups use the room identity for the op42 host and character PID for op52 guests");
        Check(ChatManager.IsQuestRoomNoticeAuthorized(1, 820300, 820300, 1, true)
            && !ChatManager.IsQuestRoomNoticeAuthorized(1, 820301, 820300, 1, true)
            && !ChatManager.IsQuestRoomNoticeAuthorized(1, 820300, 820300, 0, true)
            && !ChatManager.IsQuestRoomNoticeAuthorized(1, 820300, 820300, 1, false),
            "client opcode 199 room notice accepts only the acquired active quest room");
        Check(!QuestInstanceManager.CanDispatchQuestTransport(QuestInstanceManager.QuestTransportSignal.RoomDataInstalled,
                7, 820300, 7, 820300)
            && QuestInstanceManager.CanDispatchQuestTransport(QuestInstanceManager.QuestTransportSignal.HostRoomLive,
                7, 820300, 7, 820300)
            && QuestInstanceManager.CanDispatchQuestTransport(QuestInstanceManager.QuestTransportSignal.GatewayServerReady,
                7, 820300, 7, 820300)
            && !QuestInstanceManager.CanDispatchQuestTransport(QuestInstanceManager.QuestTransportSignal.HostRoomLive,
                7, 820300, 8, 820300)
            && !QuestInstanceManager.CanDispatchQuestTransport(QuestInstanceManager.QuestTransportSignal.HostRoomLive,
                7, 820300, 7, 820301),
            "squad quest transfer waits for the matching host room to become live");
        Check(!QuestInstanceManager.ShouldCommitQuestEntryReservation(QuestInstanceManager.QuestTransportSignal.RoomDataInstalled, true)
            && !QuestInstanceManager.ShouldCommitQuestEntryReservation(QuestInstanceManager.QuestTransportSignal.HostRoomLive, false)
            && QuestInstanceManager.ShouldCommitQuestEntryReservation(QuestInstanceManager.QuestTransportSignal.HostRoomLive, true)
            && QuestInstanceManager.ShouldCommitQuestEntryReservation(QuestInstanceManager.QuestTransportSignal.GatewayServerReady, true),
            "quest entry items remain recoverable until an authenticated host room is live");
        Check(QuestInstanceManager.CanConfirmSoloQuestHostReady(QuestInstanceManager.QuestTransportSignal.GatewayServerReady,
                1001, 820100, 820100, 1001, 1001)
            && QuestInstanceManager.CanConfirmSoloQuestHostReady(QuestInstanceManager.QuestTransportSignal.HostRoomLive,
                1001, 820100, 820100, 1001, 1001)
            && !QuestInstanceManager.CanConfirmSoloQuestHostReady(QuestInstanceManager.QuestTransportSignal.RoomDataInstalled,
                1001, 820100, 820100, 1001, 1001)
            && !QuestInstanceManager.CanConfirmSoloQuestHostReady(QuestInstanceManager.QuestTransportSignal.GatewayServerReady,
                1001, 820100, 820101, 1001, 1001),
            "solo quest readiness commits only for the authenticated bound host instance");
        Check(QuestInstanceManager.FindQuestEntryReservationHost(920002,
                new (int HostPid, IReadOnlyCollection<int> Members)[]
                {
                    (920001, new[] { 920001, 920002 }),
                    (920003, new[] { 920003, 920004 })
                }) == 920001
            && QuestInstanceManager.FindQuestEntryReservationHost(920004,
                new (int HostPid, IReadOnlyCollection<int> Members)[]
                {
                    (920001, new[] { 920001, 920002 }),
                    (920003, new[] { 920003, 920004 })
                }) == 920003
            && QuestInstanceManager.FindQuestEntryReservationHost(999999,
                new (int HostPid, IReadOnlyCollection<int> Members)[]
                {
                    (920001, new[] { 920001, 920002 })
                }) == 0,
            "pending quest entry reservations are owned by the host but discoverable from any accepted member");
        var disconnectDecisionAnswers = new Dictionary<int, byte> { [920001] = 0 };
        bool disconnectDecisionFinalized = false;
        Check(QuestInstanceManager.ApplyQuestDecisionDisconnect(920002, new[] { 920001, 920002 },
                disconnectDecisionAnswers, ref disconnectDecisionFinalized)
            && disconnectDecisionFinalized
            && disconnectDecisionAnswers.GetValueOrDefault(920002) == QuestAcquire.NotFoundPlayer
            && !QuestInstanceManager.ApplyQuestDecisionDisconnect(920002, new[] { 920001, 920002 },
                disconnectDecisionAnswers, ref disconnectDecisionFinalized),
            "disconnecting during squad quest consent immediately finalizes with NOTFOUND_PLAYER");
        var disconnectedShare = QuestManager.BuildQuestShareDisconnectResult(920002, 920001, 820102);
        Check(disconnectedShare.Pid == 920002 && disconnectedShare.QuestIndex == 820102
            && disconnectedShare.Answer == QuestAcquire.NotFoundPlayer
            && disconnectedShare.Serialize().SequenceEqual(new byte[]
            {
                46, 0xC2, 0x09, 0x0E, 0x00, 0x86, 0x83, 0x0C, 0x00, 4
            }),
            "disconnecting a quest-share recipient returns the native offline-player result to the sharer");
        Check(QuestInstanceManager.SelectQuestHostPid(7,
                [(7, System.Net.IPAddress.Parse("198.51.100.7")),
                 (8, System.Net.IPAddress.Parse("198.51.100.8"))]) == 7
            && QuestInstanceManager.SelectQuestHostPid(7,
                [(7, System.Net.IPAddress.Parse("127.0.0.3")),
                 (8, System.Net.IPAddress.Parse("127.0.0.2"))]) == 8
            && QuestInstanceManager.SelectQuestHostPid(7,
                [(7, System.Net.IPAddress.Parse("::ffff:127.0.0.3")),
                 (8, System.Net.IPAddress.Parse("::ffff:127.0.0.2"))]) == 8,
            "same-PC quest parties host on the canonical-port client while remote parties retain their leader");
        Check(new ListenDiagnostics { ClientPid = 7, ErrorCode = 1 }.Serialize()
                .SequenceEqual(new byte[] { 63, 7, 0, 0, 0, 1 }),
            "listen diagnostics relay matches native op63 db layout");
        Check(new QuestShare { Pid = 7, QuestIndex = 820102, Answer = 21 }.Serialize()
                .SequenceEqual(new byte[] { 46, 7, 0, 0, 0, 134, 131, 12, 0, 21 })
            && new QuestShareRequest { SharerPid = 7, QuestIndex = 820102 }.Serialize()
                .SequenceEqual(new byte[] { 47, 7, 0, 0, 0, 134, 131, 12, 0 }),
            "quest sharing matches native op46 ddb result and op47 dd request layouts");
        var questSquadAnswers = new QuestSquadAnswers
        {
            Opcode = 220,
            Answers = [new(7, 820102, 0), new(8, 820102, 1)]
        }.Serialize();
        Check(questSquadAnswers.SequenceEqual(new byte[] { 220, 2,
                7, 0, 0, 0, 134, 131, 12, 0, 0,
                8, 0, 0, 0, 134, 131, 12, 0, 1 }),
            "squad quest preflight/decision answers match native b + repeated ddb layout");
        Check(ChatManager.BuildQuestDecisionFailureNotice(7).Serialize()
                .SequenceEqual(new byte[] { 97, 7, 0, 0, 0, 0, 0x21 }),
            "failed squad quest consent sends the native notification that closes prompts and unlocks input");
        Check(QuestInstanceManager.QuestCreateFailureGuestPids(8, new[] { 7, 8, 7, 9, 0 })
                .SequenceEqual(new[] { 7, 9 })
            && QuestInstanceManager.QuestCreateFailureGuestPids(7, new[] { 7 }).Count == 0,
            "rejected squad room creation notifies each guest once without duplicating the host's error reply");
        var failedGuestTransport = QuestInstanceManager.BuildFailedQuestTransportGuestPackets(8)
            .Select(packet => packet.Serialize()).ToArray();
        Check(failedGuestTransport.Length == 2
            && failedGuestTransport[0].SequenceEqual(new byte[] { 52, QuestAcquire.FailLoadingRoom })
            && failedGuestTransport[1].SequenceEqual(new byte[] { 97, 8, 0, 0, 0, 0, 0x21 }),
            "pre-live quest host loss reports the join failure and releases the guest's locked city input");
        Check(new QuestJoin { Answer = 0, ServerIP = 0x0100007F, Data = new byte[16] }.Serialize()
                .SequenceEqual(new byte[] { 52, 0, 127, 0, 0, 1,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            "squad quest join matches native op52 bdm layout and low-byte-first IPv4 packing");
        Check(new QuestJoin { Answer = QuestAcquire.FailLoadingRoom }.Serialize()
                .SequenceEqual(new byte[] { 52, 16 }),
            "quest join reports the exported loading-room failure without transport data");
        Check(new QuestCanStart { QuestIndex = 820102, CanStart = 1 }.Serialize()
                .SequenceEqual(new byte[] { 53, 134, 131, 12, 0, 1 }),
            "quest room readiness matches native op53 db layout");
        var permissionBytes = new QuestPermissionRegister
        {
            Key = Enumerable.Repeat((byte)0x11, 16).ToArray(),
            Pid = 7,
            PlayerName = "A",
            Level = 9,
            Style = 2,
            Items = [new QuestCreate.Item { ItemID = 221001, InvenIndex = 3, Cell = 0, Durability = 100 }]
        }.Serialize();
        Check(permissionBytes[0] == 55
            && permissionBytes.AsSpan(1, 16).SequenceEqual(Enumerable.Repeat((byte)0x11, 16).ToArray())
            && permissionBytes.AsSpan(22, 4).SequenceEqual(new byte[] { 7, 0, 0, 0 })
            && permissionBytes.AsSpan(permissionBytes.Length - 32, 4).SequenceEqual(new byte[] { 73, 95, 3, 0 })
            && permissionBytes.AsSpan(permissionBytes.Length - 4, 4).SequenceEqual(new byte[] { 100, 0, 0, 0 }),
            "quest permission registration carries member identity, room key, and full travel loadout");
        var battlePermissionBytes = new BattleBackendPermissionRegister
        {
            Key = Enumerable.Repeat((byte)0x11, 16).ToArray(), Pid = 7, PlayerName = "A", Level = 9,
            Style = 2, Items = [new QuestCreate.Item { ItemID = 221001, InvenIndex = 3, Cell = 0, Durability = 100 }]
        }.Serialize();
        // Same MMOPermission body, except the backup block: a quest record carries type 1
        // (type, level BP word, zero carried items); HuxleyBattleGame skips backup parsing, so a
        // battle record carries type 0 and its loadout stays aligned.
        int questBackupAt = permissionBytes.Length - 32 - 7 - 4;
        Check(battlePermissionBytes[0] == 4 && battlePermissionBytes.Length == permissionBytes.Length - 3
            && battlePermissionBytes.AsSpan(1, questBackupAt - 1).SequenceEqual(permissionBytes.AsSpan(1, questBackupAt - 1))
            && permissionBytes[questBackupAt] == 1 && battlePermissionBytes[questBackupAt] == 0
            && battlePermissionBytes.AsSpan(questBackupAt + 1).SequenceEqual(permissionBytes.AsSpan(questBackupAt + 4)),
            "dedicated opcode 4 reuses the MMOPermission body with no quest backup, keeping the loadout aligned");
        var questKeyA = QuestInstanceManager.NewQuestConnectionKey();
        var questKeyB = QuestInstanceManager.NewQuestConnectionKey();
        Check(questKeyA.Length == 16 && questKeyB.Length == 16
            && questKeyA.Any(x => x != 0) && questKeyB.Any(x => x != 0)
            && !questKeyA.SequenceEqual(questKeyB),
            "each quest instance receives an independent nonzero 128-bit MMOPermission credential");
        Check(new TransportEnter { Answer = 0 }.Serialize().SequenceEqual(new byte[] { 118, 0 })
            && new TransportLeave { Answer = 0, X = 1, Y = 2, Z = 3, Yaw = 4 }.Serialize()
                .SequenceEqual(new byte[] { 119, 0, 0, 0, 128, 63, 0, 0, 0, 64,
                    0, 0, 64, 64, 4, 0 }),
            "transport enter/leave callbacks match native b and bfffw layouts");
        Check(_cityTransportCatalog.All.Count == 4
            && _cityTransportCatalog.All.OrderBy(x => x.EntryIndex).Select(x => x.EntryIndex)
                .SequenceEqual(new[] { 0, 1, 2, 3 })
            && _cityTransportCatalog.All.All(x => x.UseFee == 100),
            "all four retail public-transport routes and fares load from HuxleyCityTable");
    }
}
