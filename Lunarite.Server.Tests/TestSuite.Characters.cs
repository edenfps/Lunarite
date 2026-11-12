using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void CharacterCreationRules()
    {
        Check(_characterStore.TryCreate(_accountB, "AccountA", 6, new Custom(), out _) == CharacterStore.CreateSameName,
            "character names are globally case-insensitive unique");
        Check(_characterStore.TryCreate(_accountB, "WrongIndex", 2, new Custom(), out _) == CharacterStore.CreateWrongIndex,
            "character creation rejects non-retail species indices");
        // CP949 "한글이" plus a UHC syllable whose lead byte is 0x81 and a trail byte of 0xA0.
        byte[] koreanName = [0xC7, 0xD1, 0xB1, 0xDB, 0xC0, 0xCC, 0x81, 0xA0, 0];
        var koreanReader = new PacketReader(koreanName);
        bool koreanRead = koreanReader.TryRead(out string? decodedKorean);
        var koreanEcho = new PacketWriter();
        koreanEcho.Write(decodedKorean);
        Check(koreanRead && decodedKorean is { Length: 8 }
            && CharacterStore.IsValidName(decodedKorean)
            && koreanEcho.Buffer.SequenceEqual(koreanName)
            && !CharacterStore.IsValidName("a\u0001b") && !CharacterStore.IsValidName("a b"),
            "Korean names travel byte for byte and pass the name check; ASCII controls and spaces do not");
        const string reserved = "Nova=nova, NovaPrime=nova";
        Check(CharacterStore.IsReservedForOther("Nova", "kestrel", reserved)
            && CharacterStore.IsReservedForOther("NOVAPRIME", null, reserved)
            && !CharacterStore.IsReservedForOther("nova", "NOVA", reserved)
            && !CharacterStore.IsReservedForOther("Nova2", "kestrel", reserved)
            && !CharacterStore.IsReservedForOther("Nova", "kestrel", null),
            "reserved character names are refused to every account but their own (any case)");
        Check(CharacterStore.TrainingCenterMap(1) == 10 && CharacterStore.TrainingCenterMap(6) == 10
            && CharacterStore.TrainingCenterMap(11) == 110 && CharacterStore.TrainingCenterMap(16) == 110,
            "retail species route to the correct Sapiens or Alternatives training-center map");
        Check(_characterStore.GetCityLocation(_ownedA.Pid) == (10, 0)
            && _characterStore.GetCityLocation(_ownedB.Pid) == (10, 0),
            "new and migrated characters default to their faction training-center location");
        Check(_characterStore.SetCityLocation(_ownedA.Pid, 0, 4)
            && _characterStore.GetCityLocation(_ownedA.Pid) == (0, 4)
            && _characterStore.SetCityLocation(_ownedA.Pid, 1, 3)
            && _characterStore.GetCityLocation(_ownedA.Pid) == (1, 3)
            && !_characterStore.SetCityLocation(_ownedA.Pid, 1, 23)
            && !_characterStore.SetCityLocation(_ownedA.Pid, 100, 0)
            && !_characterStore.SetCityLocation(_ownedA.Pid, 250, 0)
            && _characterStore.GetCityLocation(_ownedA.Pid) == (1, 3),
            "retail city zone/area pairs persist while invalid areas and unavailable destinations are rejected");
        Check(CharacterStore.GetTribe(1) == 1 && CharacterStore.GetTribe(6) == 2
            && CharacterStore.GetTribe(11) == 3 && CharacterStore.GetTribe(16) == 4
            && CharacterStore.GetTribe(0) == 0,
            "character TypeIndex maps to the exact retail ETribeType used by licenses");
        Check(_characterManager.BuildAnotherPlayerInfo(_ownedA.Pid)?.Species == 1
            && _characterManager.BuildAnotherPlayerInfo(_ownedB.Pid)?.Species == 6,
            "player inspection preserves exact species TypeIndex for profile camp and species art");
        // Customize Character: an existing character of this account, same species, gets a new look.
        var face = new Custom { Hair = 4, Color = 7 };
        Check(_characterStore.TrySetCustom(_accountB, _ownedB.Name.ToUpperInvariant(), _ownedB.Index, face)?.Pid == _ownedB.Pid
                && _characterStore.Get(_ownedB.Pid)?.Custom.Hair == 4
                && _characterStore.TrySetCustom(_accountB, _ownedB.Name, 16, face) == null
                && _characterStore.TrySetCustom(_accountB, _ownedA.Name, _ownedA.Index, face) == null,
            "customize changes only this account's own character of the same species");
        var (noticeEn, noticeKo) = OnlineStatus.SplitNotice("Restarting in 1 minute.\nko:1분 후 재시작합니다.\n");
        var (onlyEn, onlyEnKo) = OnlineStatus.SplitNotice("Restart postponed.");
        var hangul = new PacketWriter(); hangul.Write("재시작");
        var latin = new PacketWriter(); latin.Write("\u00C7\u00D1");
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        Check(noticeEn == "Restarting in 1 minute." && noticeKo == "1분 후 재시작합니다."
                && onlyEn == "Restart postponed." && onlyEnKo == "Restart postponed."
                && hangul.Buffer.SequenceEqual(System.Text.Encoding.GetEncoding(949).GetBytes("재시작").Append((byte)0))
                && latin.Buffer.SequenceEqual(new byte[] { 0xC7, 0xD1, 0 }),
            "restart notices carry a Korean line, and server Hangul goes out as CP949 while client bytes stay byte for byte");
    }

    private void CharacterRosterLimit()
    {
        for (int i = 0; i < CharacterStore.MaxCharacters; i++)
            Check(_characterStore.TryCreate(_accountC, $"Limit{i}", i % 2 == 0 ? (byte)1 : (byte)6,
                new Custom(), out _) == CharacterStore.CreateOk, $"roster slot {i + 1} of 6 is accepted");
        Check(_characterStore.TryCreate(_accountC, "LimitOverflow", 1, new Custom(), out _) == CharacterStore.CreateMaxHave,
            "seventh character returns native CREATEPLAYER_MAXHAVE");
        ResetCharacter(Sender);
        ResetCharacter(Recipient);

        _playerStore.GetInventory(Sender, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 604221, Count = 3, Durability = 77 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 7, ItemId = 100100, Count = 1, Durability = 42 }
        ]);
        _playerStore.GetInventory(Recipient, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 604222, Count = 1, Durability = 100 }
        ]);
    }

    private void LevelProgressionEvents()
    {
        Check(_classB.Level == 10 && _classB.LP == 13,
            "reaching level 10 grants intervening LP plus the four-point class bonus");
        var ordinaryLevelEvents = CharacterManager.BuildLevelProgressionEvents(LevelPid, 1, 2);
        Check(ordinaryLevelEvents is [{ Event: 0x01, Value: 2 }],
            "ordinary level transition emits the native self/observer world event");
        var classLevelEvents = CharacterManager.BuildLevelProgressionEvents(LevelPid, 9, 10);
        Check(classLevelEvents is [{ Event: 0x01, Value: 10 }, { Event: 0x02, Value: 1 }],
            "class-boundary transition emits level-up followed by native B-class promotion");
        Check(CharacterManager.BuildLevelProgressionEvents(LevelPid, 10, 10).Count == 0,
            "unchanged progression emits no duplicate world event");
        Check(_questCatalog.Get(810101) is { QuestType: "QuestType_Tutorial", Level: "Q-SP_Begin" }
            && _questCatalog.Get(810101) is { ExitZoneIndex: 10, ExitAreaIndex: -1 }
            && _questCatalog.Get(860101) is { QuestType: "QuestType_Tutorial", Level: "Q-AT_Begin", ExitZoneIndex: 110 }
            && _questCatalog.Get(890022) is { ExitZoneIndex: -1, ExitAreaIndex: 13 },
            "retail catalog identifies both species tutorial quests and maps");
        Check(_questCatalog.Get(871000) is
            {
                UseContamination: true, ContaminationAmount: 10, RecommendLevel: 10,
                RecommendLevelFactor: 2.0f, MaxLife: 6, UseQuestRoom: false,
                DurabilityDecParam: 9
            }
            && _questCatalog.All.Count(x => x.UseContamination) == 177
            && _questCatalog.All.Count(x => x.RecommendLevel > 0) == 528
            && _questCatalog.All.Count(x => x.RecommendLevelFactor > 0) == 530
            && _questCatalog.All.Count(x => x.MaxLife > 0) == 392
            && _questCatalog.All.All(x => !x.ResultDrop && x.DropGradeS == 0
                && x.DropGradeA == 0 && x.DropGradeB == 0 && x.DropGradeC == 0
                && (x.ResultTimeLimits?.Count ?? 0) == 0),
            "quest catalog preserves contamination, scaling, life-limit and room metadata");
    }

    private void EconomyRatesAndPings()
    {
        Check(_ownerPings.Serialize().SequenceEqual(new byte[]
            { 224, 2, 9, 0, 0, 0, 42, 0, 0, 0, 10, 0, 0, 0, 75, 0, 0, 0 }),
            "server op224 serializes native byte-count plus PID/ping pairs");
        Check(new SimulRoomMsg { MessageType = 1, SecondsLeft = 5 }.Serialize()
                .SequenceEqual(new byte[] { 225, 1, 5, 0, 0, 0 }),
            "server op225 serializes native byte message type plus int countdown");
        var neutralRates = new PriceRate().Serialize();
        Check(neutralRates.Length == 33 && neutralRates[0] == 27
            && Enumerable.Range(0, 8).All(i => BitConverter.ToSingle(neutralRates, 1 + i * 4) == 1f),
            "server op27 publishes all eight native economy multipliers in retail order");
        _testRates = new EconomyRates.Snapshot(1.25f, 0.5f, 1.5f, 0.75f, 1.1f, 1.2f, 1.3f, 1.4f);
        Check(_economyRates.TrySetAuthoritative(_testRates)
            && _economyRates.ForCharacterIndex(1) == new EconomyRates.Faction(1.25f, 0.5f, 1.1f, 1.3f)
            && _economyRates.ForCharacterIndex(11) == new EconomyRates.Faction(1.5f, 0.75f, 1.2f, 1.4f)
            && !_economyRates.TrySetAuthoritative(_testRates with { SapiBuy = float.NaN }),
            "one validated authoritative snapshot selects all four faction service rates");
    }

    private void DeletionAndNativeIntegration()
    {
        const int transportPid = 940010;
        ResetCharacter(transportPid);
        long transportMoney = _playerStore.GetProgress(transportPid).Money;
        Check(_cityManager.TryEnterTransport(transportPid, 99, false) == 2
            && _playerStore.GetProgress(transportPid).Money == transportMoney,
            "unknown public-transport route returns native NOTFOUND without charging");
        Check(_cityManager.TryEnterTransport(transportPid, 1, true) == 4
            && _playerStore.GetProgress(transportPid).Money == transportMoney,
            "an already-riding character returns native ALREADYRODE without charging");
        Check(_cityManager.TryEnterTransport(transportPid, 1, false) == 0
            && _playerStore.GetProgress(transportPid).Money == transportMoney - 100,
            "public-transport boarding deducts the exact exported route fare");
        Check(_playerStore.TrySpendMoney(transportPid, (int)(transportMoney - 100)),
            "transport test drains the remaining balance");
        Check(_cityManager.TryEnterTransport(transportPid, 1, false) == 3
            && _playerStore.GetProgress(transportPid).Money == 0,
            "insufficient transport fare returns native ENOUGHMONEY without overdrawing");
        const int concurrentTransportPid = 940011;
        ResetCharacter(concurrentTransportPid);
        _playerStore.AddReward(concurrentTransportPid, 0, 0, 1000);
        long concurrentTransportMoney = _playerStore.GetProgress(concurrentTransportPid).Money;
        var boardingAnswers = new byte[32];
        Parallel.For(0, boardingAnswers.Length, i =>
            boardingAnswers[i] = _cityManager.TransportEnter(concurrentTransportPid, 1));
        Check(boardingAnswers.Count(x => x == 0) == 1
            && boardingAnswers.Count(x => x == 4) == boardingAnswers.Length - 1
            && _playerStore.GetProgress(concurrentTransportPid).Money == concurrentTransportMoney - 100,
            "simultaneous transport boarding records one ride and deducts exactly one fare");
    }

    private void CharacterDeletion()
    {
        const int deletionClanLeader = 920089;
        _clanStore.Leave(DeletedPid);
        _clanStore.Leave(deletionClanLeader);
        _clanStore.Create(deletionClanLeader, "DeletionLifecycleClan");
        _clanStore.Invite(deletionClanLeader, DeletedPid);
        _clanStore.AnswerInvite(DeletedPid, "DeletionLifecycleClan", true);
        Check(_characterDataLifecycle.Delete(DeletedPid),
            "permanent character deletion accepts a non-master with no external property");
        Check(_playerStore.GetInventory(DeletedPid, () => []).Count == 0
            && _licenseStore.Owned(DeletedPid).Length == 0
            && _craftStore.Learned(DeletedPid).Length == 0
            && _personalBuffStore.Active(DeletedPid).Count == 0
            && _durativeItemStore.Active(DeletedPid).Count == 0
            && _questStore.Acquired(DeletedPid).Count == 0
            && _questStore.Completed(DeletedPid).Count == 0
            && _battleResultStore.ForPid(DeletedPid).Count == 0
            && _transmitStore.List(DeletedPid).Count == 0,
            "permanent character deletion removes every unambiguously PID-owned persistent subsystem");
        var survivingMail = _transmitStore.Get(SurvivingPid, _deliveredMailSequence);
        Check(survivingMail is { SenderPid: 0, From: "deleted", CR: 25 }
            && survivingMail.Attachments is [{ Claimed: false, Item.ItemId: 603056, Item.Count: 2 }]
            && _transmitStore.TakeCR(SurvivingPid, _deliveredMailSequence)
            && !_transmitStore.TakeCR(SurvivingPid, _deliveredMailSequence)
            && _transmitStore.PeekAttachment(SurvivingPid, _deliveredMailSequence, 0)?.Item.Count == 2
            && _transmitStore.ClaimAttachment(SurvivingPid, _deliveredMailSequence, 0)
            && _transmitStore.PeekAttachment(SurvivingPid, _deliveredMailSequence, 0) == null,
            "character deletion preserves delivered mail and unclaimed value without reusing sender PID");
        Check(_friendStore.List(DeletedPid).Count == 0
            && _friendStore.List(SurvivingPid).All(x => x.Pid != DeletedPid)
            && _blockStore.List(DeletedPid).Count == 0
            && _blockStore.List(SurvivingPid).All(x => x != DeletedPid)
            && _clanStore.ForPid(DeletedPid) == null,
            "permanent character deletion removes outbound and inbound social and clan references");
        _clanStore.Leave(deletionClanLeader);

        // Opt-in native integration gate. It uses only the canonical client executable,
        // configures a real HuxleyServer through backend opcode 3, waits for opcode 8,
        // then requires the native opcode-9 acknowledgment for a full opcode-4 player record.
        if (Environment.GetEnvironmentVariable("HUXLEY_LIVE_BATTLE_TEST") == "1")
        {
            const int serverId = 217;
            const ushort gamePort = 7788;
            string executable = @"C:\Games\Huxley\Binaries\HuxleyMMOGame.exe";
            Check(File.Exists(executable), "canonical client executable exists for live battle gate");
            var backend = new Server(55902, HandlerType.Battle, BattleBackend.OnHandlePacket, _loggerFactory);
            _battleServerManager.ExpectServer(serverId, gamePort); // launched here, not by BattleInstanceManager
            Process? dedicated = null;
            try
            {
                backend.Start();
                var start = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false,
                    CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                foreach (string arg in new[] { "HuxleyServer", "BZT-019-Transponder", "SERVERS=1",
                    "BattleIndex=217", "BattleIP=127.0.0.1", "BattlePort=7788",
                    "GameIP=127.0.0.1", "GamePort=55902", "-port=7788",
                    "-LANGUAGE=INT", "-LOG", "-nosound", "-unattended" }) start.ArgumentList.Add(arg);
                dedicated = Process.Start(start);
                Check(dedicated != null, "canonical dedicated commandlet starts for live battle gate");
                var deadline = Stopwatch.StartNew();
                while (deadline.Elapsed < TimeSpan.FromSeconds(30) && !_battleServerManager.IsReady(serverId, gamePort)
                    && dedicated is { HasExited: false }) Thread.Sleep(100);
                Check(_battleServerManager.IsReady(serverId, gamePort),
                    "native commandlet accepts opcode 3 and reports opcode-8 match readiness");
                byte[] key = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();
                Check(_battleServerManager.TryRegisterPlayer(serverId, new BattleBackendPermissionRegister
                {
                    Key = key, Pid = _ownedA.Pid, PlayerName = _ownedA.Name,
                    Character = (byte)_ownedA.Index, Appearance = _ownedA.Custom, Level = 1,
                    SkillSlots = [0, 1, 2, 3, 4]
                }), "native dedicated server acknowledges opcode-4 player permission through opcode 9");
            }
            finally
            {
                if (dedicated is { HasExited: false }) dedicated.Kill(true);
                dedicated?.Dispose();
                backend.Stop();
            }
        }

        // Drive a two-member quest from the actual Game op33/op34/op199 handlers through
        // two independent encrypted sockets. The older transport test injected the room
        // directly and could not catch a failure before host creation.
        {
            const int headlessQuestIndex = 846000;
            var leaderCharacter = _characterStore.Create(939846001, "HeadlessHost", 1, new Custom());
            var guestCharacter = _characterStore.Create(939846002, "HeadlessGuest", 1, new Custom());
            foreach (int memberPid in new[] { leaderCharacter.Pid, guestCharacter.Pid })
            {
                ResetCharacter(memberPid);
                var inventory = _playerStore.GetInventory(memberPid, () => []);
                inventory.Add(new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 604221, Durability = 100 });
                inventory.Add(new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 200299, Durability = 100 });
            }

            (Client Client, System.Net.Sockets.TcpClient Peer, byte[,] Decrypt) OpenGameSocket(int id,
                string localAddress)
            {
                var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var peer = new System.Net.Sockets.TcpClient(
                    new IPEndPoint(IPAddress.Parse(localAddress), 0));
                peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                var accepted = listener.AcceptTcpClient();
                listener.Stop();
                var gameClient = new Client(id, new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), accepted);
                peer.ReceiveTimeout = 3000;
                return (gameClient, peer, gameClient.DecryptTable);
            }
            byte[] Receive((Client Client, System.Net.Sockets.TcpClient Peer, byte[,] Decrypt) link)
            {
                var stream = link.Peer.GetStream();
                Span<byte> header = stackalloc byte[2];
                stream.ReadExactly(header);
                var payload = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header) - 2];
                stream.ReadExactly(payload);
                for (int i = 0; i < payload.Length; i++) payload[i] = link.Decrypt[0, payload[i]];
                return payload;
            }
            var hostLink = OpenGameSocket(939846001, "127.0.0.2");
            var guestLink = OpenGameSocket(939846002, "127.0.0.3");
            _ = Receive(hostLink); // GameVersion
            _ = Receive(guestLink);
            _playerManager.SetOnline(leaderCharacter.Pid, hostLink.Client);
            _playerManager.SetOnline(guestCharacter.Pid, guestLink.Client);
            hostLink.Client.Player = _playerManager.Get(leaderCharacter.Pid);
            guestLink.Client.Player = _playerManager.Get(guestCharacter.Pid);
            hostLink.Client.Session.AccountId = 939846001;
            _friendStore.Delete(leaderCharacter.Pid, guestCharacter.Pid);
            Check(_friendStore.Add(leaderCharacter.Pid, guestCharacter.Pid, 0) == 0,
                "encrypted friend roster fixture records the other selected character");
            FriendList ExpectedFriendRoster(byte login)
            {
                var roster = new FriendList();
                var targetProgress = _playerStore.GetProgress(guestCharacter.Pid);
                roster.Entries.Add(new FriendWireEntry(0, guestCharacter.Pid, guestCharacter.Name,
                    (byte)guestCharacter.Index, (byte)targetProgress.Level, guestCharacter.Style, login));
                return roster;
            }
            Game.OnHandlePacket(hostLink.Client, new byte[] { 137 });
            Check(Receive(hostLink).SequenceEqual(ExpectedFriendRoster(1).Serialize()),
                "encrypted friend roster reports an online selected character");
            _playerManager.SetOffline(guestCharacter.Pid);
            _questRoomMembers.Detach(guestCharacter.Pid, guestLink.Client.Id);
            Game.OnHandlePacket(hostLink.Client, new byte[] { 137 });
            Check(Receive(hostLink).SequenceEqual(ExpectedFriendRoster(1).Serialize()),
                "encrypted friend roster keeps a quest-detached character online");
            _questRoomMembers.TryTakeDetached(guestCharacter.Pid, out _);
            Game.OnHandlePacket(hostLink.Client, new byte[] { 137 });
            Check(Receive(hostLink).SequenceEqual(ExpectedFriendRoster(0).Serialize()),
                "encrypted friend roster reports a disconnected character offline");
            _playerManager.SetOnline(guestCharacter.Pid, guestLink.Client);
            _playerManager.Get(leaderCharacter.Pid).MarkSpawned();
            _playerManager.Get(guestCharacter.Pid).MarkSpawned();
            _playerManager.Get(leaderCharacter.Pid).CityMap = 0;
            _playerManager.Get(guestCharacter.Pid).CityMap = 0;
            _playerStore.GetInventory(leaderCharacter.Pid, () => []).Add(new PlayerStore.GenericItem
                { InventoryIndex = 0, InventoryCell = 4, ItemId = 604221, Count = 2 });
            _playerStore.GetInventory(guestCharacter.Pid, () => []).Add(new PlayerStore.GenericItem
                { InventoryIndex = 0, InventoryCell = 8, ItemId = 604222, Count = 3 });
            var leaderBeforeTrade = _playerStore.GetInventorySnapshot(leaderCharacter.Pid, () => []);
            var guestBeforeTrade = _playerStore.GetInventorySnapshot(guestCharacter.Pid, () => []);
            byte[] TradeRequest(byte action, int target)
            {
                var wire = new byte[6]; wire[0] = 78; wire[1] = action;
                BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(2), target);
                return wire;
            }
            Game.OnHandlePacket(hostLink.Client, TradeRequest(0, guestCharacter.Pid));
            Check(Receive(guestLink).SequenceEqual(new Trade
                { Action = 0, Pid = leaderCharacter.Pid, Name = leaderCharacter.Name }.Serialize()),
                "encrypted two-client trade request reaches the visible peer");
            Game.OnHandlePacket(guestLink.Client, TradeRequest(1, leaderCharacter.Pid));
            Check(Receive(hostLink).SequenceEqual(new Trade
                { Action = 1, Pid = guestCharacter.Pid, Name = guestCharacter.Name }.Serialize()),
                "encrypted two-client trade acceptance opens the requester's exchange");
            Game.OnHandlePacket(hostLink.Client, new byte[] { 78, 5, 0, 4 });
            Check(Receive(hostLink).SequenceEqual(new Trade { Action = 5, Result = 0 }.Serialize())
                && Receive(guestLink)[..3].SequenceEqual(new byte[] { 85, 5, 1 }),
                "encrypted trade offer acknowledges locally and publishes the item to the peer");
            Game.OnHandlePacket(guestLink.Client, new byte[] { 78, 5, 1, 8 });
            Check(Receive(guestLink).SequenceEqual(new Trade { Action = 5, Result = 0 }.Serialize())
                && Receive(hostLink)[..4].SequenceEqual(new byte[] { 85, 5, 1, 1 }),
                "encrypted counter-offer reaches the requesting player");
            Game.OnHandlePacket(hostLink.Client, new byte[] { 78, 8 });
            Check(Receive(guestLink).SequenceEqual(new Trade { Action = 8 }.Serialize()),
                "first encrypted trade confirmation notifies the peer without committing");
            Game.OnHandlePacket(guestLink.Client, new byte[] { 78, 8 });
            Check(Receive(hostLink).SequenceEqual(new Trade { Action = 8 }.Serialize()),
                "second encrypted trade confirmation notifies the peer before settlement");
            foreach (var (link, beforeTrade, traderPid) in new[]
            {
                (hostLink, leaderBeforeTrade, leaderCharacter.Pid),
                (guestLink, guestBeforeTrade, guestCharacter.Pid)
            })
            {
                var expected = TradeManager.BuildTradeSettlementPackets(beforeTrade,
                    _playerStore.GetInventory(traderPid, () => []), _playerStore.GetProgress(traderPid).Money);
                Check(expected.Count > 1 && expected[0].Serialize().SequenceEqual(new byte[] { 85, 9 })
                    && expected.All(packet => Receive(link).SequenceEqual(packet.Serialize())),
                    "encrypted successful trade closes each menu and publishes committed item/CR state");
            }
            // Exercise the public auction handlers with two live encrypted Game connections,
            // including the seller's asynchronous sale notification and both CR updates.
            const byte auctionCell = 12;
            const int auctionPrice = 500;
            _playerStore.GetInventory(leaderCharacter.Pid, () => []).Add(new PlayerStore.GenericItem
                { InventoryIndex = 0, InventoryCell = auctionCell, ItemId = 604221, Count = 1 });
            long sellerBeforeAuction = _playerStore.GetProgress(leaderCharacter.Pid).Money;
            long buyerBeforeAuction = _playerStore.GetProgress(guestCharacter.Pid).Money;
            int liveAuctionFee = _auctionStore.RegistrationFee(leaderCharacter.Pid,
                _playerStore.GetInventory(leaderCharacter.Pid, () => []).Single(x => x.InventoryCell == auctionCell));
            using (var register = new PacketWriter())
            {
                register.Write((byte)146);
                register.Write(auctionCell);
                register.Write((byte)0);       // period (native wire order cell, period, price)
                register.Write(auctionPrice);
                Game.OnHandlePacket(hostLink.Client, register.Buffer);
            }
            Check(Receive(hostLink).SequenceEqual(new AuctionResult
                { Opcode = 164, Answer = 0, Cell = auctionCell }.Serialize())
                && Receive(hostLink).SequenceEqual(new Parameter
                { Param = Parameter.PARAMETER_MONEY,
                  Value = checked((int)(sellerBeforeAuction - liveAuctionFee)) }.Serialize()),
                "encrypted auction registration acknowledges escrow and publishes the charged seller balance");
            var liveListing = _auctionStore.All().Single(x => x.SellerPid == leaderCharacter.Pid);
            byte liveCategory = checked((byte)_itemCatalog.Get(liveListing.Item.ItemId)!.AuctionCategory);
            using (var wrongBid = new PacketWriter())
            {
                wrongBid.Write((byte)148);
                wrongBid.Write(liveListing.Index);
                wrongBid.Write((byte)((liveCategory + 1) % 27));
                Game.OnHandlePacket(guestLink.Client, wrongBid.Buffer);
            }
            Check(Receive(guestLink).SequenceEqual(new AuctionResult
                { Opcode = 166, Answer = (byte)AuctionStore.Result.AuctionNotExist }.Serialize())
                && _auctionStore.All().Any(x => x.Index == liveListing.Index)
                && _playerStore.GetProgress(guestCharacter.Pid).Money == buyerBeforeAuction,
                "encrypted mismatched-category auction bid leaves escrow and buyer CR unchanged");
            using (var bid = new PacketWriter())
            {
                bid.Write((byte)148);
                bid.Write(liveListing.Index);
                bid.Write(liveCategory);
                Game.OnHandlePacket(guestLink.Client, bid.Buffer);
            }
            Check(Receive(guestLink).SequenceEqual(new AuctionResult
                { Opcode = 166, Answer = 0, ItemIndex = liveListing.Item.ItemId }.Serialize())
                && Receive(guestLink)[0] == 82
                && Receive(guestLink).SequenceEqual(new Parameter
                { Param = Parameter.PARAMETER_MONEY,
                  Value = checked((int)(buyerBeforeAuction - auctionPrice)) }.Serialize())
                && Receive(hostLink).SequenceEqual(new AuctionNotification
                { Opcode = 169, Result = 0, ItemId = liveListing.Item.ItemId }.Serialize())
                && Receive(hostLink).SequenceEqual(new Parameter
                { Param = Parameter.PARAMETER_MONEY,
                  Value = checked((int)(sellerBeforeAuction - liveAuctionFee + auctionPrice)) }.Serialize())
                && _playerStore.GetInventory(guestCharacter.Pid, () => []).Any(x => x.ItemId == liveListing.Item.ItemId)
                && _auctionStore.All().All(x => x.Index != liveListing.Index),
                "encrypted two-client auction sale publishes the item, buyer debit, seller notice, and seller credit");
            _playerStore.GetInventory(leaderCharacter.Pid, () => []).Add(new PlayerStore.GenericItem
                { InventoryIndex = 0, InventoryCell = auctionCell, ItemId = 604222, Count = 2 });
            Check(_auctionStore.Register(leaderCharacter.Pid, leaderCharacter.Name, auctionCell, 600, 0,
                out var socketExpiredListing, DateTime.UtcNow - AuctionStore.ListingLifetime - TimeSpan.FromMinutes(1))
                == AuctionStore.Result.Success && socketExpiredListing != null,
                "auction expiration fixture escrows a past-deadline listing");
            Game.OnHandlePacket(hostLink.Client, new byte[] { 151 });
            Check(Receive(hostLink).SequenceEqual(new AuctionCheckTimeout { HasItems = true }.Serialize()),
                "encrypted auction timeout check reports expired escrow before return");
            Game.OnHandlePacket(hostLink.Client, new byte[] { 150 });
            var returnedAuctionItem = Receive(hostLink);
            Check(returnedAuctionItem[0] == 82
                && BinaryPrimitives.ReadInt32LittleEndian(returnedAuctionItem.AsSpan(1)) == 604222
                && BinaryPrimitives.ReadInt32LittleEndian(returnedAuctionItem.AsSpan(5)) == 2
                && Receive(hostLink).SequenceEqual(new AuctionNotification
                { Opcode = 168, Result = 0, ItemId = 604222 }.Serialize())
                && _auctionStore.All().All(x => x.Index != socketExpiredListing!.Index)
                && _playerStore.GetInventory(leaderCharacter.Pid, () => []).Any(x => x.ItemId == 604222 && x.Count == 2),
                "encrypted auction return-all restores expired escrow and notifies the seller");
            Game.OnHandlePacket(hostLink.Client, new byte[] { 151 });
            Check(Receive(hostLink).SequenceEqual(new AuctionCheckTimeout { HasItems = false }.Serialize()),
                "encrypted auction timeout check clears after the item is returned");
            _playerManager.Get(leaderCharacter.Pid).TryClearSpawned();
            _playerManager.Get(guestCharacter.Pid).TryClearSpawned();
            _squadManager.SetSquad(leaderCharacter.Pid, leaderCharacter.Pid,
                [leaderCharacter.Pid, guestCharacter.Pid]);
            string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
            try
            {
                using var hostNatReport = new PacketWriter();
                hostNatReport.Write((byte)155);
                hostNatReport.Write((byte)2);
                hostNatReport.Write(0x0200007F);
                hostNatReport.Write((ushort)7778);
                hostNatReport.Write(0x0200007F);
                hostNatReport.Write((ushort)7778);
                Game.OnHandlePacket(hostLink.Client, hostNatReport.Buffer);
                Check(Receive(hostLink).SequenceEqual(new NATTypeResult { Result = 1 }.Serialize()),
                    "headless quest host reports its actual UDP 7778 endpoint before mission entry");

                using var check = new PacketWriter();
                check.Write((byte)33);
                check.Write(headlessQuestIndex);
                check.Write((byte)0);
                Game.OnHandlePacket(hostLink.Client, check.Buffer);
                var expectedDecision = new QuestDecision { Pid = leaderCharacter.Pid,
                    QuestIndex = headlessQuestIndex, DifficultyLevel = 0 }.Serialize();
                Check(Receive(guestLink).SequenceEqual(expectedDecision),
                    "two-client quest preflight prompts the squad member; the leader's QuestCheck is its consent");

                using var accept = new PacketWriter();
                accept.Write((byte)34);
                accept.Write(headlessQuestIndex);
                accept.Write((byte)0);
                Game.OnHandlePacket(guestLink.Client, accept.Buffer);
                var answerList = Receive(hostLink);
                var hostCreate = Receive(hostLink);
                var hostPermission = Receive(hostLink);
                var guestRoomPermission = Receive(hostLink);
                Check(answerList[0] == 221 && hostCreate[0] == 42
                    && hostPermission[0] == 55 && guestRoomPermission[0] == 55,
                    "consent sends leader answer list then host create and both room permissions");
                var createReader = new PacketReader(hostCreate);
                createReader.Advance(3); // opcode, answer, P2P type
                Check(createReader.TryRead(out string? hostedMap)
                    && hostedMap == QuestInstanceManager.BuildQuestHostMap(_questCatalog.Get(headlessQuestIndex)!.Level!,
                        gatewayEnabled: false),
                    "gateway-disabled quest host packet admits ordinary guest JOIN on the cooked map");
                var roomKey = createReader.Read(16).ToArray();
                var hostPermissionReader = new PacketReader(hostPermission);
                hostPermissionReader.Advance(1);
                var gatewayKey = hostPermissionReader.Read(16).ToArray();
                hostPermissionReader.Advance(5); // room metadata d,b
                int gatewayPid = hostPermissionReader.Read<int>();
                var guestPermissionReader = new PacketReader(guestRoomPermission);
                guestPermissionReader.Advance(1);
                var registeredGuestKey = guestPermissionReader.Read(16).ToArray();
                guestPermissionReader.Advance(5);
                int registeredGuestPid = guestPermissionReader.Read<int>();
                Check(roomKey.Any(x => x != 0) && roomKey.SequenceEqual(gatewayKey)
                    && roomKey.SequenceEqual(registeredGuestKey)
                    && gatewayPid == headlessQuestIndex && registeredGuestPid == guestCharacter.Pid,
                    "host create and gateway/guest permissions bind the same nonzero room key to the correct PIDs");

                using var ready = new PacketWriter();
                ready.Write((byte)199);
                ready.Write(headlessQuestIndex);
                Game.OnHandlePacket(hostLink.Client, ready.Buffer);
                var dispatchPermission = Receive(hostLink);
                var guestNat = Receive(guestLink);
                var guestHoleResult = Receive(guestLink);
                var guestJoin = Receive(guestLink);
                Check(dispatchPermission.SequenceEqual(guestRoomPermission)
                    && guestNat.SequenceEqual(QuestInstanceManager.BuildQuestHostPeerEndpoints(
                        0x0200007F, 0x0200007F, 7778, 7778).Serialize())
                    && guestHoleResult.SequenceEqual(new byte[] { 125, 1, 1 })
                    && guestJoin[0] == 52,
                    "host room-ready renews guest permission then dispatches UDP 7778 and keyed join");
                var joinReader = new PacketReader(guestJoin);
                joinReader.Advance(2); // opcode, success answer
                uint joinAddress = joinReader.Read<uint>();
                Check(joinAddress == 0x0200007F && joinReader.Read(16).SequenceEqual(roomKey),
                    "guest join targets the selected 127.0.0.2 host with the same room key");

                using var guestBackup = new PacketWriter();
                guestBackup.Write((byte)152); guestBackup.Write((byte)1); guestBackup.Write((byte)2);
                guestBackup.Write(guestCharacter.Pid); guestBackup.Write((ushort)77);
                guestBackup.Write((byte)1); guestBackup.Write(604221); guestBackup.Write(3);
                Game.OnHandlePacket(hostLink.Client, guestBackup.Buffer);
                Check(_questInstanceManager.TryGetGameBackup(leaderCharacter.Pid, guestCharacter.Pid, out var storedBackup)
                    && storedBackup.Index == 1 && storedBackup.Total == 2
                    && storedBackup.EncodedLevelBp == 77
                    && storedBackup.Items.SequenceEqual(new[] { new QuestGameDataRequest.StockItem(604221, 3) }),
                    "authenticated listen host retains a guest's native mission backup without granting items");
                using var forgedBackup = new PacketWriter();
                forgedBackup.Write((byte)152); forgedBackup.Write((byte)0); forgedBackup.Write((byte)1);
                forgedBackup.Write(939999999); forgedBackup.Write((ushort)77); forgedBackup.Write((byte)0);
                Game.OnHandlePacket(hostLink.Client, forgedBackup.Buffer);
                Check(!_questInstanceManager.TryGetGameBackup(leaderCharacter.Pid, 939999999, out _),
                    "op152 cannot create a backup for a PID outside the authenticated quest instance");
            }
            finally
            {
                Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
                DisconnectHandler.HandleDisconnect(hostLink.Client);
                DisconnectHandler.HandleDisconnect(guestLink.Client);
                _squadManager.RemoveSquad(leaderCharacter.Pid);
                _playerManager.SetOffline(leaderCharacter.Pid);
                _playerManager.SetOffline(guestCharacter.Pid);
                hostLink.Client.Player = null;
                guestLink.Client.Player = null;
                hostLink.Client.Dispose();
                guestLink.Client.Dispose();
                hostLink.Peer.Dispose();
                guestLink.Peer.Dispose();
            }
        }

        var retiredCharacter = _characterStore.Create(939846003, "RetiredPid", 1, new Custom());
        Check(_characterStore.Delete(retiredCharacter.Pid), "test character can be retired");
        var replacementCharacter = _characterStore.Create(939846003, "NewPid", 1, new Custom());
        string characterSequencePath = Path.Combine(AppContext.BaseDirectory, "character-sequence.json");
        Check(replacementCharacter.Pid > retiredCharacter.Pid
            && _sqliteStore.Get("character-sequence.json")?.Contains($"\"LastPid\":{replacementCharacter.Pid}") == true
            && _atomicJson.Read<Dictionary<string, int>>(characterSequencePath)?["LastPid"] == replacementCharacter.Pid,
            "character PIDs remain monotonic in the database after deletion");
        _characterStore.Delete(replacementCharacter.Pid);
        Check(_characterStore.TryCreate(939846003, "NextPid", 1, new Custom(), out var nextCharacter)
            == CharacterStore.CreateOk && nextCharacter != null
            && nextCharacter.Pid > replacementCharacter.Pid,
            "network character-creation path also keeps PIDs monotonic after deletion");
        Check(_characterStore.TryCreate(939846003, "BothTribes", 16, new Custom(), out var otherTribe)
            == CharacterStore.CreateOk,
            "one account may hold both retail tribes");
        _characterStore.Delete(nextCharacter!.Pid);
        _characterStore.Delete(otherTribe!.Pid);
    }
}
