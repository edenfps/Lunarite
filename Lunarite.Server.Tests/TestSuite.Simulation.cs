using System.Buffers.Binary;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void SimulationRoomPackets()
    {
        var joinRoom = new JoinRoom
        {
            Room = new SimulationRoomWire(7, "Room", "Owner", 214, 1, 8, 10, 50, 1, 0, 0, 4, 0)
        };
        joinRoom.Players.Add(new SimulationPlayerWire(9, "Player", 3, 2, 1, 0, 1, 0, 1, 0,
            0x01020304, 0x0506, 0x0708090A, 0x0B0C, 55));
        var joinRoomWire = joinRoom.Serialize();
        Check(joinRoomWire[0] == 132 && joinRoomWire[1] == 0 && joinRoomWire[2] == 1
            && BinaryPrimitives.ReadInt32LittleEndian(joinRoomWire.AsSpan(joinRoomWire.Length - 4)) == 55,
            "server op132 serializes native room header and dsbbbbbbbbdwdwbd member body");
        var joinedPlayer = new SimulationPlayerWire(9, "P", 3, 2, 1, 0, 1, 0, 1, 0,
            0x01020304, 0x0506, 0x0708090A, 0x0B0C, 55);
        var joinedPlayerWire = new JoinPlayerBCChannel { RoomId = 7, Player = joinedPlayer }.Serialize();
        var roomJoinedPlayerWire = new JoinPlayerBCRoom { RoomId = 7, Player = joinedPlayer }.Serialize();
        Check(roomJoinedPlayerWire.SequenceEqual(new byte[]
                { 136, 9, 0, 0, 0, 80, 0, 3, 2, 1, 0, 1, 0, 1, 7, 0, 55, 0, 0, 0 })
            && joinedPlayerWire[0] == 147 && joinedPlayerWire[1] == 7
            && BinaryPrimitives.ReadInt32LittleEndian(joinedPlayerWire.AsSpan(2)) == 9
            && BinaryPrimitives.ReadInt32LittleEndian(joinedPlayerWire.AsSpan(joinedPlayerWire.Length - 4)) == 55
            && new LeavePlayerBCRoom { Pid = 9, RoomId = 7 }.Serialize()
                .SequenceEqual(new byte[] { 133, 9, 0, 0, 0, 7 })
            && new LeavePlayerBCChannel { RoomId = 7 }.Serialize().SequenceEqual(new byte[] { 148, 7 }),
            "Simulation room/channel join and leave broadcasts carry native scoped identities");
        Check(new JoinRoom { Result = 12 }.Serialize().SequenceEqual(new byte[] { 132, 12, 0, 0 }),
            "server op132 error branch stops after the unconditional bbb header");
        Check(new OwnerStart { Result = 0 }.Serialize().SequenceEqual(new byte[] { 143, 0 })
            && new OwnerStart { Result = 8 }.Serialize().SequenceEqual(new byte[] { 143, 8 }),
            "server op143 reports native Simulation owner-start success/readiness results");
        Check(new KickOut().Serialize().SequenceEqual(new byte[] { 154 }),
            "server op154 invokes the native parameterless kick callback");
        byte[] simulationKey = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();
        Check(new SimulationJoinMatch { HostIp = 0x01020304, Key = simulationKey }.Serialize()
                .SequenceEqual(new byte[] { 156, 4, 3, 2, 1, 1, 2, 3, 4, 5, 6, 7, 8,
                    9, 10, 11, 12, 13, 14, 15, 16 }),
            "server op156 serializes native host IPv4 plus 16-byte room key");
        var simulationState = new SimulationPlayerState
        {
            Key = simulationKey, RoomId = 7, Pid = 9, Name = "P", Character = 6,
            Appearance = new Custom()
        };
        var simulationPermissionWire = new SimulationPermissionRegister { Player = simulationState }.Serialize();
        var simulationCreateWire = new SimulationCreateMatch
        {
            Map = "Map", Key = simulationKey, MinPlayers = 1, GameType = 2, TimeLimit = 10,
            GoalScore = 50, Player = simulationState
        }.Serialize();
        Check(simulationPermissionWire.Length == 118 && simulationPermissionWire[0] == 157
            && simulationPermissionWire.AsSpan(1, 16).SequenceEqual(simulationKey)
            && simulationPermissionWire[^8..].SequenceEqual(new byte[8])
            && simulationCreateWire.Length == 135 && simulationCreateWire[0] == 155
            && simulationCreateWire[^8..].SequenceEqual(new byte[8])
            && simulationCreateWire.AsSpan(5, 16).SequenceEqual(simulationKey),
            "server op155/op157 carry the recovered Simulation host/player permission bodies");
        simulationState.Items =
        [
            new QuestCreate.Item
            {
                ItemID = 0x01020304, Count = 99, InvenIndex = 4, Cell = 2, State = 3,
                LimitTime = 0x11121314, TuneUpOptions = 0x21222324,
                Skill0 = 0x31323334, Skill1 = 0x41424344, Durability = 0x51525354
            }
        ];
        var simulationItemWire = new SimulationPermissionRegister { Player = simulationState }.Serialize();
        Check(simulationItemWire[^41..].SequenceEqual(new byte[]
            {
                0, 0,
                0, 0, 0, 0, 1, 0, 0,
                4, 3, 2, 1, 99, 0, 0, 0,
                4, 2, 3, 0, 0x14, 0x13, 0x12, 0x11,
                0x24, 0x23, 0x22, 0x21, 0x34, 0x33, 0x32, 0x31,
                0x44, 0x43, 0x42, 0x41, 0x54, 0x53, 0x52, 0x51
            }),
            "Simulation MMOPermission emits trailing byte, backup type 0 (op155 sub_11131170 / op157 sub_111C0920 both read it), counts and GenericItem records");
        Check(_battleZoneCatalog.Get(214) is { LevelName: "BZR-008-Islands", Axis: 1, MaxPlayers: 44 },
            "retail battle-zone catalog resolves Simulation map package and authoritative metadata");
        Check(_battleZoneCatalog.GetByServerId(217) is
                { EntryIndex: 217, LevelName: "BZT-019-Transponder", Enabled: true },
            "battle entry resolves its native BattleServerID to an enabled retail map");
        Check(_battleZoneCatalog.SupportsRoom(102, 16)
            && _battleZoneCatalog.SupportsRoom(104, 16)
            && _battleZoneCatalog.SupportsRoom(216, 12)
            && !_battleZoneCatalog.SupportsRoom(216, 3)
            && !_battleZoneCatalog.SupportsRoom(216, 44)
            && !_battleZoneCatalog.SupportsRoom(999999, 8)
            && !_battleZoneCatalog.SupportsRoom(214, 12)
            && !_battleZoneCatalog.SupportsRoom(106, 8) && !_battleZoneCatalog.SupportsRoom(192, 8),
            "Simulation rooms use the retail player choices and refuse unknown, disabled or map-less entries");
        _simulationMapOverrides.ResetForTest();
        Check(_simulationMapOverrides.Set(104, "bzp-004-crossfire") == "BZP-004-Crossfire"
            && _simulationMapOverrides.LevelFor(104) == "BZP-004-Crossfire"
            && _simulationMapOverrides.Set(104, "BZC-014-Slums") == null
            && _simulationMapOverrides.Set(999999, "BZP-004-Crossfire") == null
            && _simulationMapOverrides.Clear(104) && _simulationMapOverrides.LevelFor(104) == null,
            "Simulation map overrides host only present unlisted maps and can be cleared");
        Check(_cityZoneCatalog.Get(99)?.Level == "City-SP-Camptown" && _cityZoneCatalog.Get(98)?.Level == "City-SP-motorbike"
            && _cityZoneCatalog.Get(107) is { Level: "City-Eska_HQ_Lobby", ZoneType: "Zone_Building" }
            && _cityZoneCatalog.Get(109) is { Level: "City-AT-Camptown", ZoneType: "Zone_Building" }
            && _cityZoneCatalog.IsAvailableToCharacter(99, 6) && !_cityZoneCatalog.IsAvailableToCharacter(99, 11)
            && _cityZoneCatalog.IsAvailableToCharacter(109, 11),
            "the patched city table routes dead zones 98/99/107/109 to load-tested old towns per faction");
        _simulationMapOverrides.ResetForTest();
        var roomBroadcast = new CreateRoomBC
        {
            Room = new SimulationRoomWire(7, "R", "O", 214, 1, 8, 10, 50, 1, 0, 0, 4, 0),
            CurrentPlayers = 2, SapiPlayers = 1, AlterPlayers = 1, OwnerLevel = 3, AverageLevel = 4
        }.Serialize();
        Check(roomBroadcast.SequenceEqual(new byte[]
                { 145, 7, 82, 0, 79, 0, 214, 0, 0, 0, 1, 10, 0, 0, 8, 2,
                  50, 0, 1, 3, 1, 1, 4, 4 })
            && new DestroyRoomBC { RoomId = 7 }.Serialize().SequenceEqual(new byte[] { 146, 7 })
            && new ChangeGameStateBC { RoomId = 7, GameState = 1 }.Serialize()
                .SequenceEqual(new byte[] { 149, 7, 1 })
            && new ChangePlayerStateBC { Pid = 9, State = 2 }.Serialize()
                .SequenceEqual(new byte[] { 150, 9, 0, 0, 0, 2 }),
            "server op145 matches native room field order and op146/op149/op150 expose state callbacks");
        Check(new ChangeRoomInfo().Serialize().SequenceEqual(new byte[] { 135, 0 })
            && new ChangeRoomInfoBC { RoomId = 7, Map = 214, GameType = 1, GoalTime = 10,
                GoalScore = 50, MaxPlayers = 8, LimitClass = 4 }.Serialize()
                .SequenceEqual(new byte[] { 139, 7, 214, 0, 0, 0, 1, 10, 50, 0, 8, 4 }),
            "server op135/op139 room-change reply and bdbbwbb broadcast match native layouts");
        Check(new UpdateBZRoomState { State = 1, TimeoutMilliseconds = 15000 }.Serialize()
                .SequenceEqual(new byte[] { 144, 1, 0x98, 0x3A, 0, 0 }),
            "server op144 serializes native battle-room state and millisecond timeout");
        Check(new BRTZoneInfo
            {
                MaxTeam = 8, Sapiens = 3, Alternatives = 4, ReadyButtonOn = 1,
                EnemyState = 1, Started = 0, LeaderPid = 0x01020304
            }.Serialize().SequenceEqual(new byte[]
                { 151, 8, 3, 4, 1, 1, 0, 0, 0, 4, 3, 2, 1 }),
            "server op151 serializes aligned 12-byte native _BattleZoneInfo_ blob");
        _ownerPings = new OwnerPingTime();
        _ownerPings.Entries.Add(new SimulationPing(9, 42));
        _ownerPings.Entries.Add(new SimulationPing(10, 75));
    }

    private void BattleBackendPackets()
    {
        byte[] simulationBackupBody = new byte[2 + 2 + 2 + 1 + 31 * 2];
        BinaryPrimitives.WriteUInt16LittleEndian(simulationBackupBody, 12);
        BinaryPrimitives.WriteUInt16LittleEndian(simulationBackupBody.AsSpan(2), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(simulationBackupBody.AsSpan(4), 321);
        simulationBackupBody[6] = 5;
        simulationBackupBody[7] = 0xA1;
        simulationBackupBody[38] = 0xB2;
        var simulationBackupReader = new PacketReader(simulationBackupBody);
        Check(BattleGameDataRequest.TryDeserialize(ref simulationBackupReader, out var simulationBackup)
            && simulationBackup.TeamScore0 == 12 && simulationBackup.TeamScore1 == 9
            && simulationBackup.RemainingTime == 321 && simulationBackup.BackupType == 5
            && simulationBackup.PlayerCount == 2 && simulationBackup.PlayerRecords.Length == 62
            && simulationBackup.PlayerRecords[0] == 0xA1 && simulationBackup.PlayerRecords[31] == 0xB2,
            "native op152 wwwbm battle backup parses count-less 31-byte player records exactly");
        var malformedSimulationBackupReader = new PacketReader(new byte[2 + 2 + 2 + 1 + 30]);
        Check(!BattleGameDataRequest.TryDeserialize(ref malformedSimulationBackupReader, out _),
            "native op152 battle backup rejects partial player records");
        Check(new BattleBackendVerify { Answer = 0 }.Serialize().SequenceEqual(new byte[] { 0, 0 }),
            "battle backend verification accepts with native server opcode 0 byte result");
        Check(new BattleBackendStart
        {
            MapIndex = 217, MapUrl = "BZT-019-Transponder", MaxPlayers = 40,
            GoalScore = 400, TimeLimitMinutes = 20, WarmingUp = 0
        }.Serialize().SequenceEqual(new byte[] {
            3, 19, 217, (byte)'B', (byte)'Z', (byte)'T', (byte)'-', (byte)'0', (byte)'1', (byte)'9',
            (byte)'-', (byte)'T', (byte)'r', (byte)'a', (byte)'n', (byte)'s', (byte)'p', (byte)'o',
            (byte)'n', (byte)'d', (byte)'e', (byte)'r', 0, 40, 0x90, 0x01, 20, 0
        }), "battle backend start order serializes recovered opcode 3 bbsbwbb layout");
        Check(new BattleBackendOccupation { Species = 2 }.Serialize().SequenceEqual(new byte[] { 11, 2 }),
            "battle backend occupation serializes native opcode 11 species byte");
        byte[] battleRecord = Enumerable.Range(1, 31).Select(x => (byte)x).ToArray();
        byte[] plainPermission = new SimulationPermissionRegister { Player = new SimulationPlayerState { Pid = 7 } }.Serialize();
        byte[] restoredPermission = new SimulationPermissionRegister
            { Player = new SimulationPlayerState { Pid = 7, BattleBackup = battleRecord } }.Serialize();
        int backupAt = plainPermission.Length - 2 - 7;
        Check(restoredPermission.Length == plainPermission.Length + 31 && plainPermission[backupAt] == 0
            && restoredPermission[backupAt] == 2
            && restoredPermission.AsSpan(backupAt + 1, 31).SequenceEqual(battleRecord),
            "re-hosted Simulation permissions carry the player's 31-byte battle record as backup type 2");
        string dedicatedEngineIni = BattleInstanceManager.BuildServerEngineIni(
            "[URL]\r\nPort=7777\r\n[Engine.Engine]\r\nNetworkDevice=HuxleyGame.HuxleyP2PNetDriver\r\nbUseSound=True\r\n"
            + "[Other]\r\nNetworkDevice=Keep.Me\r\n");
        Check(dedicatedEngineIni == "[URL]\r\nPort=7777\r\n[Engine.Engine]\r\nNetworkDevice=IpDrv.TcpNetDriver\r\nbUseSound=True\r\n"
            + "[Other]\r\nNetworkDevice=Keep.Me\r\n",
            "dedicated battle instances use the stock UDP driver, whose LowLevelDestroy closes the pre-travel socket");
        Check(new CityWarp { Result = 2 }.Serialize().SequenceEqual(new byte[] { 226, 2 }),
            "server op226 city-warp answer serializes the native one-byte result");
        Check(new QuestEnterRequestNotice { Key = 0x01020304, QuestIndex = 0x05060708 }.Serialize()
            .SequenceEqual(new byte[] { 44, 4, 3, 2, 1, 8, 7, 6, 5 }),
            "server op44 quest-entry prompt retains the native dd wire layout");
    }

    private void ServerLogAndGmChat()
    {
        string logTestDirectory = Path.Combine(Path.GetTempPath(), "HuxleyLogTest-" + Guid.NewGuid().ToString("N"));
        string logTestFile = Lunarite.Log.EnableFile(logTestDirectory);
        Lunarite.Log.WriteInfo("Persistent log test {0}", 42);
        Lunarite.Log.WriteDebug("Secret debug payload {0}", 99);
        Lunarite.Log.DisableFile();
        Check(File.ReadAllText(logTestFile).Contains("Persistent log test 42")
            && !File.ReadAllText(logTestFile).Contains("Secret debug payload"),
            "opt-in server log persists operational messages without raw debug payloads");
        File.Delete(logTestFile);
        Directory.Delete(logTestDirectory);

        // GM chat commands (the socket client is closed by now, so replies are dropped; check effects).
        _serverEvents.ResetForTest();
        _simulationMapOverrides.ResetForTest();
        Check(_gmCommands.RunGmCommand(_questSocketClient, "gm", "!pcroom on") && _serverEvents.PcRoomActive
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", "!pcroom off") && !_serverEvents.PcRoomActive
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", "!simmap 104 BZP-004-Crossfire")
            && _simulationMapOverrides.LevelFor(104) == "BZP-004-Crossfire"
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", "!simmap 104 off") && _simulationMapOverrides.LevelFor(104) == null
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", "!goto 8")
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", "!back")
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", "!unknown"),
            "GM chat commands toggle the PC-room event, set and clear map overrides, and refuse unsafe travel");
        var giftTarget = _characterStore.All().First(c => c.Name.Length > 0 && !c.Name.Contains(' '));
        int giftsBefore = _payItemStore.Get(giftTarget.OwnerUserId).Count;
        bool giftCommand = _gmCommands.RunGmCommand(_questSocketClient, "gm", $"!givepaid {giftTarget.Name} 604019 2")
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", $"!givepaid {giftTarget.Name} 999999999 1")
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", "!givepaid nobody-by-this-name 604019");
        var gifts = _payItemStore.Get(giftTarget.OwnerUserId).Skip(giftsBefore).ToList();
        Check(giftCommand && gifts.Count == 2
            && gifts.All(x => x is { ItemId: 604019, CashName: "Event Gift", SenderAccountId: "GM" }),
            "!givepaid puts event items in the account's paid storage and refuses unknown items and names");
        foreach (var gift in gifts) _payItemStore.TryTake(giftTarget.OwnerUserId, gift.StorageSeq, gift.StorageItemSeq, out _);
        byte styleBefore = giftTarget.Style;
        bool styleSet = _gmCommands.RunGmCommand(_questSocketClient, "gm", $"!style avenger {giftTarget.Name}")
            && _characterStore.Get(giftTarget.Pid)?.Style == 2
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", $"!style knight {giftTarget.Name}")
            && _characterStore.Get(giftTarget.Pid)?.Style == 2;
        _characterStore.SetStyle(giftTarget.Pid, styleBefore);
        Check(styleSet, "!style sets a named character's combat style and refuses unknown styles");
        long moneyBefore = _playerStore.GetProgress(giftTarget.Pid).Money;
        bool moneyGiven = _gmCommands.RunGmCommand(_questSocketClient, "gm", $"!money 25000 {giftTarget.Name}")
            && _playerStore.GetProgress(giftTarget.Pid).Money == moneyBefore + 25000
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", $"!money -25000 {giftTarget.Name}")
            && _playerStore.GetProgress(giftTarget.Pid).Money == moneyBefore
            && _gmCommands.RunGmCommand(_questSocketClient, "gm", $"!money lots {giftTarget.Name}")
            && _playerStore.GetProgress(giftTarget.Pid).Money == moneyBefore;
        Check(moneyGiven, "!money adds and removes a named character's CR and refuses bad amounts");
        string[] styleNames = ["", "CombatStyle_Enforcer", "CombatStyle_Avenger"];
        Check(GmCommands.KitItems.All(kit => kit.Value.Items.All(id =>
                _itemCatalog.Get(id) is { EquipLevel: 30 } item && item.CombatStyle == styleNames[kit.Value.Style])),
            "every !kit item is level-30 gear of that kit's combat style");
        Check(CityManager.IsUnstuckCommand("/unstuck") && CityManager.IsUnstuckCommand(" /UNSTUCK ") && CityManager.IsUnstuckCommand("!unstuck")
            && !CityManager.IsUnstuckCommand("/unstuck now") && !CityManager.IsUnstuckCommand("unstuck"),
            "/unstuck is recognised for every player, case-insensitively, and only as a whole command");
        _serverEvents.ResetForTest();
        _simulationMapOverrides.ResetForTest();
        Console.WriteLine("PASS: persistent mail, quest-progress, and UE3 net-field integration checks");
    }
}
