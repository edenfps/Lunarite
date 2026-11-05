using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void BattleResultParsing()
    {
        byte[] battleResultBody = new byte[4 + 1 + 1 + 27 + 12 + 48 + 104];
        BinaryPrimitives.WriteInt32LittleEndian(battleResultBody, 920001);
        battleResultBody[4] = 1; battleResultBody[5] = 9; battleResultBody[^1] = 0xA5;
        const int battleBlockOffset = 4 + 1 + 1 + 27 + 12;
        BinaryPrimitives.WriteInt32LittleEndian(battleResultBody.AsSpan(battleBlockOffset), 920001);
        battleResultBody[battleBlockOffset + 5] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(battleResultBody.AsSpan(battleBlockOffset + 30), 777);
        var battleResultReader = new PacketReader(battleResultBody);
        Check(BattleResultRequest.TryDeserialize(ref battleResultReader, out var battleResult)
            && battleResult.Pid == 920001 && battleResult.Species == 1 && battleResult.Result == 9
            && battleResult.Durability.Length == 27 && battleResult.WeaponKills.Length == 12
            && battleResult.Battle.Length == 48 && battleResult.Detail.Length == 104 && battleResult.Detail[^1] == 0xA5
            && battleResult.EmbeddedPid == 920001 && battleResult.GameType == 3 && battleResult.AwardedLevelBp == 777,
            "native op114/backend-op3 battle result parses fixed blocks and recovered progression offsets exactly");
    }

    private void TcpFraming()
    {
        var framer = new TcpPacketFramer();
        Check(framer.Append([6, 0, 1]).Count == 0,
            "TCP framer retains a split packet prefix/body");
        var splitFrame = framer.Append([2, 3, 4]);
        Check(splitFrame.Count == 1 && splitFrame[0].SequenceEqual(new byte[] { 6, 0, 1, 2, 3, 4 }),
            "TCP framer completes a packet across reads");
        var joinedFrames = framer.Append([4, 0, 5, 6, 5, 0, 7, 8, 9]);
        Check(joinedFrames.Count == 2
            && joinedFrames[0].SequenceEqual(new byte[] { 4, 0, 5, 6 })
            && joinedFrames[1].SequenceEqual(new byte[] { 5, 0, 7, 8, 9 }),
            "TCP framer separates coalesced packets from one read");
        var battleFramer = new TcpPacketFramer(3, TcpPacketFramer.MaximumServerPacketLength);
        var battleVerifyFrame = battleFramer.Append([
            17, 0, 6, 19, 217, (byte)'1', (byte)'2', (byte)'7', (byte)'.', (byte)'0',
            (byte)'.', (byte)'0', (byte)'.', (byte)'1', 0, 0x6C, 0x1E
        ]);
        Check(battleVerifyFrame.Count == 1
            && battleVerifyFrame[0].AsSpan(2).SequenceEqual(new byte[] {
                6, 19, 217, (byte)'1', (byte)'2', (byte)'7', (byte)'.', (byte)'0',
                (byte)'.', (byte)'0', (byte)'.', (byte)'1', 0, 0x6C, 0x1E
            }),
            "CServerTcpLink framing preserves the native unencrypted opcode/body and 4095-byte limit");

        byte[] createAckWire = [0x84, 0x83, 0x0C, 0x00, 0x01];
        var createAckReader = new PacketReader(createAckWire);
        Check(QuestCreateAck.TryDeserialize(ref createAckReader, out var createAck)
            && createAck.QuestIndex == 820100 && createAck.Ready == 1,
            "native op51 db quest-create acknowledgment parses exactly");
    }

    private void BattleEntryPackets()
    {
        Check(new BattleReserve { Axis = 2, Reserved = 1, Answer = 0 }.Serialize()
                .SequenceEqual(new byte[] { 29, 2, 1, 0 }),
            "battle reservation callback serializes native bbb order");
        Check(new Battle { Answer = 16 }.Serialize().SequenceEqual(new byte[] { 31, 16 }),
            "battle entry failure uses native op31 answer-only branch");
        Check(new Battle
            {
                Answer = 0, ServerIp = 0x0100007F, ServerPort = 7788,
                Key = Enumerable.Range(0, 16).Select(x => (byte)x).ToArray()
            }.Serialize().SequenceEqual(new byte[]
            {
                31, 0, 0x7F, 0, 0, 1, 0x6C, 0x1E,
                0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
            }), "battle entry success uses native op31 bdwm16 endpoint and key layout");
        var axisInfos = new BattleAxisInfos();
        axisInfos.Entries.Add(new BattleAxisEntry(2, 216, 0, 0, 0, 0, 1));
        Check(axisInfos.Serialize().SequenceEqual(new byte[] { 32, 1, 2, 216, 0, 0, 0, 0, 0, 0, 0, 1 }),
            "battle axis directory serializes native count plus bbbbbdb entry");
        var fieldInfos = new BattleFieldInfos();
        fieldInfos.Entries.Add(new BattleFieldEntry(216, 3, 0, 0, 0, 0, 0, 0, 0));
        Check(fieldInfos.Serialize().SequenceEqual(new byte[] { 33, 1, 216, 3, 0, 0, 0, 0, 0, 0, 0 }),
            "battlefield directory serializes native count plus nine-byte entry");
    }

    private void BattleRecordPackets()
    {
        Check(new ListenerNotice { Message = "Test" }.Serialize()
                .SequenceEqual(new byte[] { 222, (byte)'T', (byte)'e', (byte)'s', (byte)'t', 0 }),
            "listener notice matches native op222 null-terminated string layout");
        Check(new CharBattleRecord { Sheet = 1, Data = new byte[76] }.Serialize() is var recordWire
            && recordWire.Length == 81 && recordWire[0] == 223 && recordWire[1] == 1 && recordWire[2] == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(recordWire.AsSpan(3)) == 76,
            "battle-record response uses native op223 bbw header and summary struct size");
        _battleBlock = new byte[48];
        _battleBlock[5] = 1; // EBZType_TDM
        BinaryPrimitives.WriteInt32LittleEndian(_battleBlock.AsSpan(6), 1);
        BinaryPrimitives.WriteInt32LittleEndian(_battleBlock.AsSpan(10), 7);
        BinaryPrimitives.WriteInt32LittleEndian(_battleBlock.AsSpan(14), 2);
        BinaryPrimitives.WriteInt32LittleEndian(_battleBlock.AsSpan(26), BitConverter.SingleToInt32Bits(125f));
        BinaryPrimitives.WriteInt32LittleEndian(_battleBlock.AsSpan(30), 900);
        _weaponBlock = new byte[12]; _weaponBlock[0] = 4; // [category, kills:u16] x 4
        BinaryPrimitives.WriteUInt16LittleEndian(_weaponBlock.AsSpan(1), 7);
        _battleResultStore.Add(new BattleResultRequest(920041, 0, 9, new byte[27], _weaponBlock, _battleBlock, new byte[104]));
        var summaryRecord = _battleResultStore.BuildRecordSheet(920041, 1);
        var localRecord = _battleResultStore.BuildRecordSheet(920041, 3);
        Check(summaryRecord.Length == 76
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(8)) == 1
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(12)) == 1
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(28)) == 125
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(40)) == 7
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(56)) == 7
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(0)) == 4
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(4)) == 1
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(68)) == -1
            && BinaryPrimitives.ReadInt32LittleEndian(summaryRecord.AsSpan(72)) == -1,
            "battle telemetry populates summary rounds, wins, seconds, kills, weapon kills, favorites and no-spree markers");
        Check(localRecord.Length == 84
            && BinaryPrimitives.ReadInt32LittleEndian(localRecord.AsSpan(12)) == 1
            && BinaryPrimitives.ReadInt32LittleEndian(localRecord.AsSpan(16)) == 1
            && BinaryPrimitives.ReadInt32LittleEndian(localRecord.AsSpan(20)) == 900,
            "simulation TDM telemetry populates the native local BZT mode record");
        _battleResultStore.ResetRecord(QuestRecordPid);
    }

    private void BattleRecordResetAndInspection()
    {
        Directory.Delete(_patchRoot, true);
        _battleResultStore.Add(new BattleResultRequest(920042, 0, 9, new byte[27], _weaponBlock, _battleBlock, new byte[104]));
        Check(_battleResultStore.ResetRecord(920042) && _battleResultStore.ForPid(920042).Count == 0
            && !_battleResultStore.ResetRecord(920042),
            "battle-record reset deletes only an existing character record and reports an empty record");
        var replayReport = new BattleResultRequest(920043, 0, 9, new byte[27], _weaponBlock, _battleBlock, new byte[104]);
        _battleResultStore.ResetRecord(920043);
        Check(_battleResultStore.TryAddUnique(replayReport, TimeSpan.FromMinutes(5))
            && !_battleResultStore.TryAddUnique(replayReport, TimeSpan.FromMinutes(5))
            && _battleResultStore.ForPid(920043).Count == 1,
            "identical op114 battle-result retries are persisted and eligible for quest progress exactly once");
        _battleResultStore.ResetRecord(920043);
        var anotherRecord = _battleResultStore.BuildAnotherPlayerRecord(920041);
        var anotherWire = new ReceivedAnotherPlayerInfo
        {
            Name = "Player", Species = 1, Level = 12, Style = 2, Rank = 3, ClanMark = 44,
            BattleRecord = anotherRecord
        }.Serialize();
        Check(anotherRecord.Length == 32
            && BinaryPrimitives.ReadInt32LittleEndian(anotherRecord.AsSpan(8)) == 1
            && BinaryPrimitives.ReadInt32LittleEndian(anotherRecord.AsSpan(12)) == 1
            && anotherWire[0] == 17 && anotherWire[1] == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(anotherWire.AsSpan(anotherWire.Length - 34, 2)) == 32,
            "player inspection returns native op17 profile plus aggregated 32-byte battle record");
        Check(new ReceivedAnotherPlayerInfo { Result = 1 }.Serialize().SequenceEqual(new byte[] { 17, 1 }),
            "unknown player inspection uses native op17 error branch");
        Check(GmCommands.IsGmLogin("Gmone", "admin, gmone") && !GmCommands.IsGmLogin("gmone3", "admin,gmone")
            && !GmCommands.IsGmLogin("gmone", null) && !GmCommands.IsGmLogin("", "gmone"),
            "GM commands are limited to the logins in HUXLEY_GM_LOGINS");
        _accountStore.Register("gmstatustest", "secret", out _gmStatusUserId);
    }

    private void BattlefieldFrontAndRewards()
    {
        _battleFront.ResetForTest();
        long frontNow = 1_000_000;
        _battleFront.Now = () => frontNow;
        byte seededDominant = _battleFront.DominantSpecies();
        Check(_battleFront.Chain(2).Select(z => z.EntryIndex).SequenceEqual(new[] { 215, 216, 217, 218 })
            && _battleFront.BattlegroundOf(2) == 216
            && _battleFront.OccupantOf(215) == 1 && _battleFront.OccupantOf(216) == 1
            && _battleFront.OccupantOf(217) == 2 && _battleFront.OccupantOf(218) == 2
            && _battleFront.StatusOf(216, false) == BattleFront.FieldStatus.Joinable
            && _battleFront.StatusOf(216, true) == BattleFront.FieldStatus.Battle
            && _battleFront.StatusOf(217, false) == BattleFront.FieldStatus.Safe
            && _battleFront.CanEnter(216) && !_battleFront.CanEnter(217) && _battleFront.CanEnter(104),
            "war front seeds axis 2 from cooked base species with Harbor (AxisStart) as the battleground");
        var frontLoss = _battleFront.RecordResult(216, 2);
        Check(frontLoss == new BattleFront.Outcome(216, 1, 2, 215)
            && _battleFront.OccupantOf(216) == 2 && _battleFront.BattlegroundOf(2) == 215
            && _battleFront.StatusOf(215, false) == BattleFront.FieldStatus.Recess
            && _battleFront.RecessMinutesLeft(215) == 10 && !_battleFront.CanEnter(215)
            && _battleFront.RecordResult(215, 2) == null && _battleFront.RecordResult(216, 1) == null,
            "an Alternative win captures Harbor, pushes the front toward the Sapiens side, and rests it");
        Check(seededDominant == 0 && _battleFront.DominantSpecies() == 2,
            "server-list camp is the faction holding more contested front fields (tie = none)");
        frontNow += 10 * 60_000L;
        var frontDefense = _battleFront.RecordResult(215, 1);
        Check(_battleFront.CanEnter(216) == false && frontDefense == new BattleFront.Outcome(215, 1, 1, 216)
            && _battleFront.OccupantOf(215) == 1 && _battleFront.BattlegroundOf(2) == 216,
            "after recess a Sapiens defense keeps Junkyard and pushes the front back to Harbor");
        Check(_battleFront.Axes.SequenceEqual(new[] { 0, 2 })
            && _battleFront.Chain(0).Select(z => z.EntryIndex).SequenceEqual(new[] { 208, 209, 210, 213 })
            && _battleFront.Chain(1).Count == 0
            && _battleFront.StatusOf(214, false) == BattleFront.FieldStatus.Safe
            && _battleFront.BattlegroundOf(0) == 209
            && _battleFront.StatusOf(209, false) == BattleFront.FieldStatus.Joinable
            && _battleFront.StatusOf(208, false) == BattleFront.FieldStatus.Safe
            && _battleZoneCatalog.Get(104) is { OnFront: false },
            "war-front chains follow cooked LinkedBattleServers, including the AxisID-0 radar chain the UI lists, "
            + "and leave out the bEnabled=false Islands axis");
        Check(_battleManager.ResolveBattleRequest(0) == 209 && _battleManager.ResolveBattleRequest(216) == 216
            && _battleManager.ResolveBattleRequest(104) == 104,
            "battlefield-map entry requests carry the axis id and resolve to that axis's open battleground");
        var harborZone = _battleZoneCatalog.Get(216)!;
        Check(harborZone.LinkedServers.SequenceEqual(new[] { 215, 217 })
            && harborZone.SapiWinRewards.SequenceEqual(new[] { (609101, 3), (500001, 6), (609201, 1) })
            && harborZone.AlterLoseRewards.SequenceEqual(new[] { (609101, 1), (500001, 2) }),
            "battle-zone catalog decodes LinkedBattleServers and the ItemNCount reward arrays");
        Check(_battleZoneCatalog.Get(208)?.RegisterFee == 4 && _battleZoneCatalog.Get(211)?.RegisterFee == 2
            && _battleZoneCatalog.Get(216)?.RegisterFee == 0,
            "battle-zone catalog reads the Battle Medal register fee");
        Check(_battleServerManager.BattleFieldRewardItems(harborZone, 1, true, 7)
                .SequenceEqual(new[] { (609101, 3), (609201, 1), (BattleServerManager.LunariteItem, 7) })
            && _battleServerManager.BattleFieldRewardItems(harborZone, 2, false, 0).SequenceEqual(new[] { (609101, 1) })
            && _battleServerManager.BattleFieldRewardItems(harborZone, 1, true, 9999).Last() == (BattleServerManager.LunariteItem, 8 + 30 + 1)
            && _battleServerManager.BattleFieldRewardItems(harborZone, 0, true, 7).Count == 0
            && _battleServerManager.BattleFieldRewardItems(_battleZoneCatalog.Get(104)!, 1, true, 7).Count == 0,
            "battle-field results pay table medals/certificates and the reported Lunarite (capped)");
        _battleFront.ResetForTest();
        _battleFront.Now = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _questEntryReservationStore.ResetForTest();
        ResetCharacter(EntryRefundPid);
        _playerStore.GetInventory(EntryRefundPid, () => []);
    }

    private void BattleResultSettlement()
    {
        var qtBattleBlock = new byte[48];
        qtBattleBlock[46] = 0x34; qtBattleBlock[47] = 0x12;
        Check(new BattleResultRequest(1, 1, 9, new byte[27], new byte[12], qtBattleBlock, new byte[104]).QT == 0x1234,
            "battle results read the PRI QT (PCr) word at battle-block offset 46");
        const int battleWearPid = 920470;
        ResetCharacter(battleWearPid);
        var battleWearInventory = _playerStore.GetInventory(battleWearPid, () => []);
        battleWearInventory.Add(new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 110101, Count = 1, Durability = 90 });
        battleWearInventory.Add(new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 110101, Count = 1, Durability = 80 });
        battleWearInventory.Add(new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 4, ItemId = 110101, Count = 1, Durability = 50 });
        byte[] battleWearBlock = new byte[27];
        byte[][] battleWearRecords = [[3, 0, 70], [3, 1, 0], [3, 2, 0], [3, 3, 0], [4, 0, 0], [4, 1, 0], [4, 2, 0], [4, 3, 0], [4, 4, 60]];
        for (int i = 0; i < battleWearRecords.Length; i++) battleWearRecords[i].CopyTo(battleWearBlock, i * 3);
        _battleManager.ApplyBattleDurability(new BattleResultRequest(battleWearPid, 1, 9, battleWearBlock, new byte[12], new byte[48], new byte[104]));
        var wornInventory = _playerStore.GetInventory(battleWearPid, () => []);
        Check(BattleManager.BattleDurabilityRecords(battleWearBlock).Count == 9
            && wornInventory.Single(x => x.InventoryIndex == 3 && x.InventoryCell == 0).Durability == 70
            && wornInventory.Single(x => x.InventoryIndex == 3 && x.InventoryCell == 1).Durability == 80
            && wornInventory.Single(x => x.InventoryIndex == 4 && x.InventoryCell == 4).Durability == 50,
            "battle results wear gear by slot, never repair it, and ignore an empty weapon slot's zero");
        Check(_simulationManager.SkirmishRewardItems(true, 30, 8).SequenceEqual(new[] { (BattleManager.BattleMedalItem, 3), (BattleServerManager.LunariteItem, 8) })
            && _simulationManager.SkirmishRewardItems(false, 12, 5).SequenceEqual(new[] { (BattleManager.BattleMedalItem, 1), (BattleServerManager.LunariteItem, 5) })
            && _simulationManager.SkirmishRewardItems(false, 12, 99).Last() == (BattleServerManager.LunariteItem, 8)
            && _simulationManager.SkirmishRewardItems(true, 5, 0).SequenceEqual(new[] { (BattleManager.BattleMedalItem, 2) }),
            "time-limited Skirmish pays retail Battle Medals and the reported Lunarite (capped at 8)");
        Check(_questStore.RecordEnemyDown(QuestPid, QuestIndex)
            && _questStore.UpdateRally(QuestPid, QuestIndex, 1, 2, 2, 90, 100, 200, 0, 4, 5000)
            && _questStore.GetProgress(QuestPid, QuestIndex).KilledMonsters == 4,
            "authenticated enemy-down count survives stale zero gateway telemetry");
        Check(_questStore.UpdateWeaponKills(QuestPid, QuestIndex,
                new (byte Category, ushort Kills)[] { (1, 17), (7, 300), (0, 400) })
            && _questStore.UpdateWeaponKills(QuestPid, QuestIndex,
                new (byte Category, ushort Kills)[] { (1, 3), (7, 301) })
            && _questStore.GetProgress(QuestPid, QuestIndex).WeaponKills.Count == 2
            && _questStore.GetProgress(QuestPid, QuestIndex).WeaponKills[1] == 17
            && _questStore.GetProgress(QuestPid, QuestIndex).WeaponKills[7] == 301,
            "native terminal weapon-use telemetry persists absolute category kills without stale regression");
        _questStore.Reset(DirectFieldTelemetryPid);
        Check(!_questStore.RecordEnemyDown(DirectFieldTelemetryPid, _directFieldQuest)
            && _questStore.RecordEnemyDown(DirectFieldTelemetryPid, _directFieldQuest,
                authenticatedDirectFieldRun: true)
            && _questStore.GetProgress(DirectFieldTelemetryPid, _directFieldQuest).KilledMonsters == 1
            && !_questStore.RecordEnemyDown(DirectFieldTelemetryPid, QuestIndex,
                authenticatedDirectFieldRun: true),
            "authenticated direct-entry field kills persist without opening unacquired non-field quests");
        Check(!_questStore.UpdateWeaponKills(DirectFieldTelemetryPid, _directFieldQuest,
                new (byte Category, ushort Kills)[] { (1, 5) })
            && _questStore.UpdateWeaponKills(DirectFieldTelemetryPid, _directFieldQuest,
                new (byte Category, ushort Kills)[] { (1, 5) }, authenticatedDirectFieldRun: true)
            && _questStore.GetProgress(DirectFieldTelemetryPid, _directFieldQuest).WeaponKills[1] == 5
            && !_questStore.UpdateWeaponKills(DirectFieldTelemetryPid, QuestIndex,
                new (byte Category, ushort Kills)[] { (1, 5) }, authenticatedDirectFieldRun: true),
            "authenticated direct-entry field result records weapon kills without opening unacquired non-field quests");
        Check(_questStore.UpdateDirectFieldRally(DirectFieldTelemetryPid, _directFieldQuest,
                1, 2, 1, 4, -3, 7, 0, 8, 90)
            && _questStore.GetProgress(DirectFieldTelemetryPid, _directFieldQuest) is
                { RallyPoint: 1, MaxRallyPoint: 2, Complete: 1, SquadBP: 4,
                    BP: -3, QT: 7, KilledMonsters: 1, SpawnedMonsters: 8, PlayTime: 90 }
            && !_questStore.UpdateDirectFieldRally(DirectFieldTelemetryPid, QuestIndex,
                1, 2, 1, 4, -3, 7, 0, 8, 90)
            && !_questStore.UpdateDirectFieldRally(DirectFieldTelemetryPid, _directFieldQuest,
                3, 2, 1, 4, -3, 7, 0, 8, 90),
            "direct-field rally telemetry preserves op56 kills and rejects non-field or invalid reports");
    }

    private (Client Client, System.Net.Sockets.TcpClient Peer) OpenBattleTestGameClient(int pid)
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var peer = new System.Net.Sockets.TcpClient();
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = listener.AcceptTcpClient();
        listener.Stop();
        var client = new Client(pid, new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), accepted);
        peer.ReceiveTimeout = 3000;
        _playerManager.SetOnline(pid, client);
        client.Player = _playerManager.Get(pid);
        _ = ReceiveBattleTestPacket(client, peer); // GameVersion
        return (client, peer);
    }

    private static byte[] ReceiveBattleTestPacket(Client client, System.Net.Sockets.TcpClient peer)
    {
        var stream = peer.GetStream();
        Span<byte> header = stackalloc byte[2];
        stream.ReadExactly(header);
        var payload = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header) - 2];
        stream.ReadExactly(payload);
        for (int i = 0; i < payload.Length; i++) payload[i] = client.DecryptTable[0, payload[i]];
        return payload;
    }

    private static byte[] ReceiveBattleTestOpcode(Client client, System.Net.Sockets.TcpClient peer, byte opcode)
    {
        for (int i = 0; i < 30; i++)
        {
            byte[] packet = ReceiveBattleTestPacket(client, peer);
            if (packet[0] == opcode) return packet;
        }
        throw new InvalidOperationException($"No op{opcode} packet was sent.");
    }

    private static bool BattleTestQuiet(System.Net.Sockets.TcpClient peer)
    {
        int timeout = peer.ReceiveTimeout;
        peer.ReceiveTimeout = 300;
        try { peer.GetStream().ReadExactly(new byte[2]); return false; }
        catch (IOException) { return true; }
        finally { peer.ReceiveTimeout = timeout; }
    }

    private static Process StartBattleTestPlaceholderProcess() => Process.Start(new ProcessStartInfo("ping")
    {
        ArgumentList = { "-n", "120", "127.0.0.1" }, CreateNoWindow = true, UseShellExecute = false,
        RedirectStandardOutput = true
    })!;

    // The backend link has no cipher or credentials: only a local instance this server launched is
    // verified, and its op3/op4 reports count only for players it admitted through op9.
    private void BattleBackendTrust()
    {
        Check(Server.ListenAddress(HandlerType.Battle, null).Equals(IPAddress.Loopback)
            && Server.ListenAddress(HandlerType.Battle, "127.0.0.1").Equals(IPAddress.Loopback)
            && Server.ListenAddress(HandlerType.Battle, "192.168.1.20").Equals(IPAddress.Any)
            && Server.ListenAddress(HandlerType.Game, null).Equals(IPAddress.Any),
            "the battle backend listens on loopback unless BATTLE_BACKEND_IP names another address");
        Check(BattleBackendVerifyHandler.IsLocalPeer(IPAddress.Loopback)
            && BattleBackendVerifyHandler.IsLocalPeer(IPAddress.Parse("::ffff:127.0.0.1"))
            && !BattleBackendVerifyHandler.IsLocalPeer(IPAddress.Parse("203.0.113.9")),
            "battle backend peers must be on this machine");

        const byte linkServer = 104;
        const ushort linkPort = 7880;
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new System.Net.Sockets.TcpClient();
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var accepted = listener.AcceptTcpClient();
        listener.Stop();
        using var link = new Client(954600001, new Server(0, HandlerType.Battle, static (_, _) => { }, _loggerFactory), accepted);
        byte[] verify = [6, BattleServerManager.BackendVersion, linkServer,
            (byte)'1', (byte)'2', (byte)'7', (byte)'.', (byte)'0', (byte)'.', (byte)'0', (byte)'.', (byte)'1', 0,
            (byte)(linkPort & 0xFF), (byte)(linkPort >> 8)];
        BattleBackend.OnHandlePacket(link, verify);
        bool strangerRefused = !_battleServerManager.IsServerLink(link.Id);
        _battleServerManager.ExpectServer(linkServer, linkPort);
        BattleBackend.OnHandlePacket(link, verify);
        Check(strangerRefused && _battleServerManager.IsServerLink(link.Id),
            "battle backend op6 verifies only a BattleIndex/BattlePort this server launched");

        var member = _characterStore.Create(939946001, "BattleLinkMember", 1, new Custom());
        ResetCharacter(member.Pid);
        _playerStore.GetInventory(member.Pid, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = InventoryType.Main, InventoryCell = 0, ItemId = BattleManager.BattleMedalItem, Count = 5 });
        int MedalCount() => _playerStore.GetInventory(member.Pid, () => [])
            .Where(x => x.ItemId == BattleManager.BattleMedalItem).Sum(x => x.Count);
        byte[] useItem = new byte[11];
        useItem[0] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(useItem.AsSpan(1), member.Pid);
        useItem[5] = InventoryType.Main; useItem[6] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(useItem.AsSpan(7), 1);
        byte[] result = new byte[1 + 4 + 1 + 1 + 27 + 12 + 48 + 104];
        result[0] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(1), member.Pid);
        result[5] = 1; result[6] = 9;
        const int resultBattleOffset = 1 + 4 + 1 + 1 + 27 + 12;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(resultBattleOffset), member.Pid);
        result[resultBattleOffset + 5] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(resultBattleOffset + 30), 777);
        long bpBefore = _playerStore.GetProgress(member.Pid).BP;
        BattleBackend.OnHandlePacket(link, useItem);
        BattleBackend.OnHandlePacket(link, result);
        bool outsiderIgnored = MedalCount() == 5 && _playerStore.GetProgress(member.Pid).BP == bpBefore;
        _battleServerManager.RecordLinkPlayer(link.Id, member.Pid);
        BattleBackend.OnHandlePacket(link, useItem);
        BattleBackend.OnHandlePacket(link, result);
        Check(outsiderIgnored && MedalCount() == 4 && _playerStore.GetProgress(member.Pid).BP == bpBefore + 777,
            "battle backend op3 results and op4 item use apply only to players the link admitted");

        BattleBackend.OnHandlePacket(link, [0, 2, 1]);
        bool dominating = _battleServerManager.DominatingVictoryOf(link.Id);
        BattleBackend.OnHandlePacket(link, [0, 2, 0]);
        Check(dominating && !_battleServerManager.DominatingVictoryOf(link.Id),
            "battle backend op0 records each match's dominating-victory flag for its results");
        _battleServerManager.RemoveLink(link.Id);
        _battleServerManager.ForgetServer(linkServer, linkPort);
        _battleResultStore.ResetRecord(member.Pid);
        ResetCharacter(member.Pid);
    }

    // HuxleyMultiTeamGame pays WinTeamMedalCount+1 / LoseTeamMedalCount-1 Medals of Valor on a
    // dominating victory, and always credits the rewards: what does not fit arrives by mail.
    private void BattlefieldRewardDelivery()
    {
        var harborZone = _battleZoneCatalog.Get(216)!;
        Check(_battleServerManager.BattleFieldRewardItems(harborZone, 1, true, 7, dominatingVictory: true)
                .SequenceEqual(new[] { (BattleServerManager.MedalOfValorItem, 4), (609201, 1), (BattleServerManager.LunariteItem, 7) })
            && _battleServerManager.BattleFieldRewardItems(harborZone, 2, false, 0, dominatingVictory: true).Count == 0
            && _battleServerManager.BattleFieldRewardItems(harborZone, 2, false, 0, dominatingVictory: false)
                .SequenceEqual(new[] { (BattleServerManager.MedalOfValorItem, 1) }),
            "a dominating victory adds a Medal of Valor for winners and takes one from losers");

        var fullPlayer = _characterStore.Create(939946002, "BattleFullBag", 1, new Custom());
        ResetCharacter(fullPlayer.Pid);
        var inventory = _playerStore.GetInventory(fullPlayer.Pid, () => []);
        int capacity = _playerStore.MainCapacity(fullPlayer.Pid);
        for (int cell = 0; cell < capacity - 1; cell++)
            inventory.Add(new PlayerStore.GenericItem
                { InventoryIndex = InventoryType.Main, InventoryCell = (byte)cell, ItemId = 110101, Count = 1, Durability = 100 });
        byte[] wonBlock = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(wonBlock.AsSpan(6), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wonBlock.AsSpan(34), 7);
        _battleServerManager.GrantBattleFieldRewards(new BattleResultRequest(fullPlayer.Pid, 1, 9,
            new byte[27], new byte[12], wonBlock, new byte[104]), harborZone);
        var rewardMail = _transmitStore.List(fullPlayer.Pid).SingleOrDefault();
        Check(_playerStore.GetInventory(fullPlayer.Pid, () => [])
                .Where(x => x.ItemId == BattleServerManager.MedalOfValorItem).Sum(x => x.Count) == 3
            && rewardMail != null
            && rewardMail.Attachments.Select(x => (x.Item.ItemId, x.Item.Count))
                .SequenceEqual(new[] { (609201, 1), (BattleServerManager.LunariteItem, 7) }),
            "battle-field rewards that do not fit the inventory are mailed instead of lost");
        _transmitStore.DeleteCharacter(fullPlayer.Pid);
        ResetCharacter(fullPlayer.Pid);
    }

    private void BattlefieldEntryAndReservations()
    {
        var battleInstanceManager = _services.GetRequiredService<BattleInstanceManager>();
        var mapAvailable = battleInstanceManager.MapAvailable;
        battleInstanceManager.MapAvailable = _ => true;
        _battleFront.ResetForTest();
        long frontNow = 5_000_000;
        _battleFront.Now = () => frontNow;
        var player = _characterStore.Create(939946003, "BattleEntrant", 1, new Custom());
        var bystander = _characterStore.Create(939946004, "BattleBystander", 1, new Custom());
        ResetCharacter(player.Pid);
        _playerStore.GetInventory(player.Pid, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = InventoryType.Main, InventoryCell = 0, ItemId = BattleManager.BattleMedalItem, Count = 10 });
        int Medals() => _playerStore.GetInventory(player.Pid, () => [])
            .Where(x => x.ItemId == BattleManager.BattleMedalItem).Sum(x => x.Count);
        var (client, peer) = OpenBattleTestGameClient(player.Pid);
        Process? shared = null, solo = null;
        try
        {
            // Mine 209 is axis 0's battleground and costs 4 medals; no dedicated server is ready
            // for it here, so the player registration fails after admission.
            Check(_battleFront.BattlegroundOf(0) == 209 && _battleZoneCatalog.Get(209)?.RegisterFee == 4,
                "the radar axis opens on Mine 209 with a 4-medal register fee");
            shared = StartBattleTestPlaceholderProcess();
            battleInstanceManager.AddInstanceForTest(209, 7881, "BZT-Test", shared, bystander.Pid);
            _battleManager.SendBattleAdmission(client, 209);
            Check(ReceiveBattleTestOpcode(client, peer, 31)[1] == BattleAnswer.LoadingRoomFailed
                && Medals() == 10 && !battleInstanceManager.IsInBattle(player.Pid),
                "a battlefield entry the dedicated server does not accept takes no Battle Medals");
            battleInstanceManager.Leave(bystander.Pid);

            solo = StartBattleTestPlaceholderProcess();
            battleInstanceManager.AddInstanceForTest(209, 7882, "BZT-Test", solo, player.Pid);
            bool rejoined = battleInstanceManager.TryEnter(player.Pid, 209, out var admission)
                    == BattleInstanceManager.EnterResult.Admitted
                && admission is { Rejoin: true, Port: 7882 };
            _battleManager.SendBattleAdmission(client, 209);
            Check(rejoined && ReceiveBattleTestOpcode(client, peer, 31)[1] == BattleAnswer.LoadingRoomFailed
                && !solo.HasExited && battleInstanceManager.IsMemberOf(player.Pid, 209) && Medals() == 10,
                "repeating entry to the battlefield a player is in keeps its instance and charges no second fee");

            // Commit to the first battlefield: a click on another one is refused with
            // BattleMsg[6] while the player is in its instance or was admitted moments ago.
            var other = StartBattleTestPlaceholderProcess();
            battleInstanceManager.AddInstanceForTest(210, 7883, "BZT-Other", other);
            var switched = battleInstanceManager.TryEnter(player.Pid, 210, out _);
            battleInstanceManager.MarkJoined(player.Pid);
            battleInstanceManager.AgeAdmissionForTest(player.Pid, BattleInstanceManager.JoinGrace + TimeSpan.FromMinutes(1));
            var joinedSwitch = battleInstanceManager.TryEnter(player.Pid, 210, out _);
            Check(switched == BattleInstanceManager.EnterResult.Committed
                && joinedSwitch == BattleInstanceManager.EnterResult.Committed
                && battleInstanceManager.IsMemberOf(player.Pid, 209) && !battleInstanceManager.IsMemberOf(player.Pid, 210),
                "a player in one battlefield cannot enter another (BattleMsg[6] already participating)");
            battleInstanceManager.BeginEntryForTest(bystander.Pid, 210);
            var duplicate = battleInstanceManager.TryEnter(bystander.Pid, 209, out _);
            battleInstanceManager.EndEntryForTest(bystander.Pid);
            Check(duplicate == BattleInstanceManager.EnterResult.AlreadyEntering && !battleInstanceManager.IsInBattle(bystander.Pid),
                "clicks while a battlefield entry is still launching are dropped");
            battleInstanceManager.Leave(player.Pid);
            battleInstanceManager.AddInstanceForTest(209, 7884, "BZT-Test", StartBattleTestPlaceholderProcess(), player.Pid);
            battleInstanceManager.AgeAdmissionForTest(player.Pid, BattleInstanceManager.JoinGrace + TimeSpan.FromMinutes(1));
            var released = battleInstanceManager.TryEnter(player.Pid, 210, out var moved);
            Check(released == BattleInstanceManager.EnterResult.Admitted && moved is { Port: 7883 }
                && battleInstanceManager.IsMemberOf(player.Pid, 210),
                "a player who never reached its battlefield may pick another after the join grace");
            battleInstanceManager.Leave(player.Pid);

            _battleManager.SendBattleAdmission(client, 210);
            bool offFront = ReceiveBattleTestOpcode(client, peer, 31)[1] == BattleAnswer.NonCombatArea;
            _battleFront.RecordResult(209, 1);
            _battleManager.SendBattleAdmission(client, 210);
            Check(offFront && _battleFront.BattlegroundOf(0) == 210
                && ReceiveBattleTestOpcode(client, peer, 31)[1] == BattleAnswer.NoCombatYet,
                "war-front refusals answer BattleMsg 23 off the battleground and 3 while it rests");
            Check(_battleManager.BuildBattleAxisEntry(0, player.Pid).EstimatedWaitMinutes
                    == _battleFront.RecessMinutesLeft(210)
                && _battleFront.RecessMinutesLeft(210) > 0,
                "the axis panel's estimated wait is in minutes, as native OnBattleAxisInfos reads it");

            byte[] Reserve(byte axis, byte reserve)
            {
                var reader = new PacketReader(new[] { axis, reserve });
                BattleReserveHandler.HandlePacket(client, ref reader);
                return ReceiveBattleTestOpcode(client, peer, 29);
            }
            // Axis 2 (Harbor 216) is open: a reservation is invited at once.
            Check(Reserve(2, 1).SequenceEqual(new byte[] { 29, 2, 1, 0 })
                && ReceiveBattleTestOpcode(client, peer, 30).SequenceEqual(new byte[] { 30, 2, 216 })
                && !_battleManager.TryGetReservation(player.Pid, out _),
                "reserving an axis whose battleground is open sends the battle invite at once");
            _battleManager.SendBattleFrontInvites();
            _battleManager.Reserve(player.Pid, 2);
            Check(_battleManager.SendBattleFrontInvites() >= 1
                && ReceiveBattleTestOpcode(client, peer, 30).SequenceEqual(new byte[] { 30, 2, 216 }),
                "a later sweep still invites reservations on an axis that stayed open");
            Check(Reserve(1, 1).SequenceEqual(new byte[] { 29, 1, 1, 1 }),
                "the disabled Islands axis cannot be reserved");

            // Axis 0 rests on 210; a reservation there holds, and another axis is double-booking.
            Check(Reserve(0, 1).SequenceEqual(new byte[] { 29, 0, 1, 0 }) && BattleTestQuiet(peer)
                && Reserve(2, 1).SequenceEqual(new byte[] { 29, 2, 1, BattleAnswer.DoubleBooking })
                && _battleManager.TryGetReservation(player.Pid, out byte keptAxis) && keptAxis == 0,
                "reserving a second axis answers BattleMsg 20 and keeps the first reservation");
            Reserve(0, 0);
            _battleFront.RecordResult(216, 2); // Harbor falls; axis 2 rests on 215
            frontNow += 60 * 60_000L;
            _battleManager.SendBattleFrontInvites(); // 215's first recess has ended
            _battleFront.RecordResult(215, 2); // the chain end keeps 215 and rests it again
            Check(Reserve(2, 1).SequenceEqual(new byte[] { 29, 2, 1, 0 }) && BattleTestQuiet(peer)
                && _battleFront.BattlegroundOf(2) == 215,
                "a reservation waits while the battleground rests on the same map");
            frontNow += 60 * 60_000L;
            Check(_battleManager.SendBattleFrontInvites() >= 1
                && ReceiveBattleTestOpcode(client, peer, 30).SequenceEqual(new byte[] { 30, 2, 215 }),
                "the end of a same-map recess invites the axis's reservations");
        }
        finally
        {
            battleInstanceManager.Leave(player.Pid);
            battleInstanceManager.Leave(bystander.Pid);
            shared?.Dispose();
            solo?.Dispose();
            _battleManager.CancelReservation(player.Pid);
            _playerManager.SetOffline(player.Pid);
            client.Dispose();
            peer.Dispose();
            battleInstanceManager.MapAvailable = mapAvailable;
            _battleFront.ResetForTest();
            _battleFront.Now = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ResetCharacter(player.Pid);
        }
    }
}
