using System.Buffers.Binary;
using System.Net;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Network.Ue3;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void NatReportsAndCoopPermissions()
    {
        var questSocketListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        questSocketListener.Start();
        _questSocketPeer = new System.Net.Sockets.TcpClient();
        _questSocketPeer.Connect(IPAddress.Loopback,
            ((IPEndPoint)questSocketListener.LocalEndpoint).Port);
        _questSocketAccepted = questSocketListener.AcceptTcpClient();
        questSocketListener.Stop();
        _questSocketClient = new Client(OfflineGuestClientId,
            new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), _questSocketAccepted);
        _questSocketPeer.ReceiveTimeout = 3000;
        _questPeerStream = _questSocketPeer.GetStream();
        _decryptTable = _questSocketClient.DecryptTable;
        _ = ReadQuestSocketPacket(); // GameVersion, sent by the Client constructor.
        _questSocketClient.Session.AccountId = 920008;
        Game.OnHandlePacket(_questSocketClient, _natReportWire.Buffer);
        byte[] natAcknowledgment = ReadQuestSocketPacket();
        Check(_questSocketClient.Session.NatEndpoint is { } acceptedNatReport
            && acceptedNatReport.PublicPort == 7778 && acceptedNatReport.LocalPort == 7779
            && natAcknowledgment.SequenceEqual(new NATTypeResult { Result = 1 }.Serialize()),
            "authenticated Game link retains NAT endpoints and completes the native test phase");
        var zeroLocalNatReport = _natReportWire.Buffer.ToArray();
        zeroLocalNatReport.AsSpan(8, 4).Clear();
        Game.OnHandlePacket(_questSocketClient, zeroLocalNatReport);
        var zeroLocalAcknowledgment = ReadQuestSocketPacket();
        Check(_questSocketClient.Session.NatEndpoint is { } zeroLocalAccepted
            && zeroLocalAccepted.LocalAddress == 0 && zeroLocalAccepted.LocalPort == 7779
            && P2PManager.ReportedNatLocalAddress(zeroLocalAccepted.LocalAddress) == null
            && zeroLocalAcknowledgment.SequenceEqual(new NATTypeResult { Result = 1 }.Serialize()),
            "native NAT report with failed local-host lookup retains its usable public endpoint");
        var invalidNatReport = _natReportWire.Buffer.ToArray();
        invalidNatReport[1] = 0;
        Game.OnHandlePacket(_questSocketClient, invalidNatReport);
        var invalidNatAddress = _natReportWire.Buffer.ToArray();
        invalidNatAddress.AsSpan(2, 4).Clear();
        Game.OnHandlePacket(_questSocketClient, invalidNatAddress);
        Check(_questSocketClient.Session.NatEndpoint is { } retainedNatReport
            && retainedNatReport.NatType == 2 && retainedNatReport.LocalAddress == 0,
            "invalid NAT type or zero public address cannot overwrite an accepted quest endpoint");
        // Live client op196 follows the NAT test with two relay/my-address records.
        // Parsing it must not replace the separate authenticated op155 NAT endpoint.
        var networkAddressWire = Convert.FromHexString(
            "C4027F000001E3C47F000001611E7F000001E4C47F000001611E");
        Game.OnHandlePacket(_questSocketClient, networkAddressWire);
        Check(_questSocketClient.Session.NetworkAddresses is { } networkReport
            && networkReport.Entries.Count == 2
            && networkReport.Entries[0].RelayPort == 50403
            && networkReport.Entries[0].MyPort == 7777
            && networkReport.Entries[1].RelayPort == 50404
            && networkReport.Entries[1].MyPort == 7777
            && _questSocketClient.Session.NatEndpoint is { } unchangedNat
            && unchangedNat.PublicPort == 7778,
            "authenticated op196 retains native relay/my endpoints without replacing op155 NAT authority");
        Game.OnHandlePacket(_questSocketClient, [118, 1]);
        Check(_questSocketClient.Session.HolePunchAcknowledged,
            "authenticated native op118 records the UDP hole-punch test ACK");
        _questSocketClient.Session.NatEndpoint = null;
        _questSocketClient.Session.NetworkAddresses = null;
        _questSocketClient.Session.HolePunchAcknowledged = false;
        _questSocketClient.Session.AccountId = null;
        Game.OnHandlePacket(_questSocketClient, networkAddressWire);
        Game.OnHandlePacket(_questSocketClient, [118, 1]);
        Check(_questSocketClient.Session.NetworkAddresses == null,
            "unauthenticated op196 cannot establish a client relay-address report");
        Check(!_questSocketClient.Session.HolePunchAcknowledged,
            "unauthenticated op118 cannot confirm UDP hole punching");
        var refreshPermission = new QuestPermissionRegister
        {
            Pid = OfflineGuestPid,
            Key = _guestTravelKey,
            PlayerName = "HeadlessGuest"
        };
        var offlineTransport = new QuestInstanceManager.QuestTransport
        {
            QuestIndex = 820300,
            HostPid = OfflineHostPid,
            Members = [OfflineHostPid, OfflineGuestPid],
            Key = _guestTravelKey,
            HostIp = 0x0100007Fu,
            HostPeerIp = 0x0300007Fu,
            HostJoinIp = 0x0200007Fu,
            HostPublicPort = 7778,
            HostPeerPort = 7779,
            MemberPermissions = [refreshPermission]
        };
        _questInstanceManager.SetTransport(offlineTransport);
        _playerManager.SetOnline(OfflineGuestPid, _questSocketClient);
        _playerManager.SetOnline(OfflineHostPid, _questSocketClient);
        var permissionRefreshTask = _questInstanceManager.RefreshQuestMemberPermissionsUntilReady(
            OfflineHostPid, offlineTransport, TimeSpan.FromMilliseconds(500));
        Check(ReadQuestSocketPacket().SequenceEqual(refreshPermission.Serialize()),
            "co-op host receives a fresh native guest permission while loading");
        bool offlineDispatched = _questInstanceManager.ConfirmQuestHostReady(OfflineHostPid, 820300,
            QuestInstanceManager.QuestTransportSignal.HostRoomLive);
        permissionRefreshTask.GetAwaiter().GetResult();
        Check(ReadQuestSocketPacket().SequenceEqual(refreshPermission.Serialize()),
            "co-op host renews the guest permission at room-ready before guest travel");
        // The host (same test socket) first gets the guest's endpoints, op128 like the guest's.
        byte[] hostPeerList = ReadQuestSocketPacket();
        Check(hostPeerList.Length == _guestTravelSeparateJoin[0].Length && hostPeerList[0] == _guestTravelSeparateJoin[0][0],
            "host-ready dispatch gives the host each guest's P2P endpoints (op128) so it hole-punches first");
        var offlineWirePackets = new[] { ReadQuestSocketPacket(), ReadQuestSocketPacket(), ReadQuestSocketPacket() };
        Check(offlineDispatched && offlineWirePackets[0].SequenceEqual(_guestTravelSeparateJoin[0])
            // op125 suspends the guest's P2P receive thread before it browses.
            && offlineWirePackets[1].SequenceEqual(new HoleResult { Result = 1, Playable = 1 }.Serialize())
            && offlineWirePackets[2].SequenceEqual(_guestTravelSeparateJoin[1]),
            "host-ready dispatch keeps NAT UDP endpoints apart from keyed UE3 join on the real Game socket");
        // The live share UI accepts through op36 QuestAcquire, not the commented-out
        // SendQuestShareConsent. Reuse this encrypted socket to verify the sharer callback.
        _questManager.SetPendingShare(OfflineGuestClientId, (OfflineHostPid, 820102));
        _playerManager.SetOnline(OfflineHostPid, _questSocketClient);
        _questManager.FinishQuestShareAcquire(_questSocketClient, OfflineGuestPid, 820103, QuestAcquire.Ok);
        Check(_questManager.HasPendingShare(OfflineGuestClientId),
            "acquiring a different quest does not consume a pending squad quest share");
        _questManager.FinishQuestShareAcquire(_questSocketClient, OfflineGuestPid, 820102, QuestAcquire.Ok);
        Check(!_questManager.HasPendingShare(OfflineGuestClientId)
            && ReadQuestSocketPacket().SequenceEqual(new QuestShare
                { Pid = OfflineGuestPid, QuestIndex = 820102, Answer = QuestAcquire.Ok }.Serialize()),
            "active op36 quest-share acceptance sends native op46 result to its sharer exactly once");
    }

    private void UnknownEnemyDrop()
    {
        _questRoomMembers.SetInstance(_detachedGuestClientId, OfflineHostPid);
        _questStore.Reset(OfflineGuestPid);
        _detachedGuestResult = new byte[197];
        _detachedGuestResult[0] = 60;
        BinaryPrimitives.WriteInt32LittleEndian(_detachedGuestResult.AsSpan(1), OfflineGuestPid);
        _detachedGuestResult[5] = 9; // native fixed equipment-record count
        _detachedGuestResult[33] = 1; // first weapon-use category follows the 27 equipment bytes
        BinaryPrimitives.WriteUInt16LittleEndian(_detachedGuestResult.AsSpan(34), 5);
        Game.OnHandlePacket(_questSocketClient, _detachedGuestResult);
        Check(_questStore.GetProgress(OfflineGuestPid, _detachedReportQuest).WeaponKills.GetValueOrDefault((byte)1) == 5
            && !_questStore.Acquired(OfflineGuestPid).Contains(_detachedReportQuest),
            "authenticated listen-host op60 persists a detached guest's direct-field weapon kills without acquisition");
        _questStore.Reset(OfflineHostPid);
        _fieldEnemyDown = new byte[5];
        _fieldEnemyDown[0] = 56;
        BinaryPrimitives.WriteInt32LittleEndian(_fieldEnemyDown.AsSpan(1), 9050111);
        Game.OnHandlePacket(_questSocketClient, _fieldEnemyDown);
        byte[] fieldDropReply = ReadQuestSocketPacket();
        Check(fieldDropReply[0] == 59
            && _questStore.GetProgress(OfflineHostPid, _detachedReportQuest).KilledMonsters == 1
            && !_questStore.Acquired(OfflineHostPid).Contains(_detachedReportQuest),
            "authenticated direct-field op56 records a kill and replies with the native enemy-drop packet");
        // Per-player loot: the same kill is rolled again for the guest and dropped with its owner.
        byte[] guestDropReply = ReadQuestSocketPacket();
        Check(guestDropReply[0] == 59
            && BinaryPrimitives.ReadInt32LittleEndian(fieldDropReply.AsSpan(1)) != OfflineGuestPid
            && BinaryPrimitives.ReadInt32LittleEndian(guestDropReply.AsSpan(1)) == OfflineGuestPid
            && _questStore.GetProgress(OfflineHostPid, _detachedReportQuest).KilledMonsters == 1,
            "a co-op kill sends one op59 per participant, each owned by that participant, and counts once");
        BinaryPrimitives.WriteInt32LittleEndian(_fieldEnemyDown.AsSpan(1), int.MaxValue);
        Game.OnHandlePacket(_questSocketClient, _fieldEnemyDown);
        // A guest still shares this instance, so even the empty reply is ownerless.
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestListenDropItem
                { Opcode = 59, Pid = 0 }.Serialize())
            && _questStore.GetProgress(OfflineHostPid, _detachedReportQuest).KilledMonsters == 1,
            "unknown op56 enemy returns an empty native drop reply without inventing a field kill");
        _directFieldRally = new byte[36];
        _directFieldRally[0] = 54;
        BinaryPrimitives.WriteInt32LittleEndian(_directFieldRally.AsSpan(1), OfflineHostPid);
    }

    private void ScriptedPickups()
    {
        Game.OnHandlePacket(_questSocketClient, _directFieldRally);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestProgressAnswer { Opcode = 56, Answer = 0,
                QuestIndex = _detachedReportQuest, Index = 0, Count = 0, State = 0 }.Serialize())
            && _playerStore.GetProgress(OfflineGuestPid).BP == _detachedGuestBpBefore + 120
            && _playerStore.GetProgress(OfflineGuestPid).QT == _detachedGuestQtBefore + 7
            && _playerStore.GetProgress(OfflineHostPid).BP == _offlineHostBpAfterSettlement,
            "detached guest's settlement op54 credits only the guest's field BP/QT");
        _questRoomMembers.SetInstance(OfflineGuestClientId, 0);
        Game.OnHandlePacket(_questSocketClient, _fieldEnemyDown);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestListenDropItem
                { Opcode = 59, Pid = _detachedReportQuest }.Serialize())
            && _questStore.GetProgress(OfflineHostPid, _detachedReportQuest).KilledMonsters == 1,
            "op56 without a live instance cannot award another field kill or enemy drop");
        _questRoomMembers.SetInstance(OfflineGuestClientId, OfflineHostPid);
        int unauthorizedScriptItemBefore = _playerStore.GetInventory(OfflineHostPid, () => [])
            .Where(item => item.ItemId == 604221).Sum(item => item.Count);
        var unsupportedScriptGrant = new byte[9];
        unsupportedScriptGrant[0] = 46;
        BinaryPrimitives.WriteInt32LittleEndian(unsupportedScriptGrant.AsSpan(1), 604221);
        BinaryPrimitives.WriteInt32LittleEndian(unsupportedScriptGrant.AsSpan(5), 5);
        Game.OnHandlePacket(_questSocketClient, unsupportedScriptGrant);
        Check(_playerStore.GetInventory(OfflineHostPid, () => [])
                .Where(item => item.ItemId == 604221).Sum(item => item.Count)
                == unauthorizedScriptItemBefore,
            "authenticated quest op46 cannot mint an item absent from all cooked quest MoveItem actions");
        var scriptedDropRequest = new byte[5];
        scriptedDropRequest[0] = 57;
        BinaryPrimitives.WriteInt32LittleEndian(scriptedDropRequest.AsSpan(1), 82165);
        Game.OnHandlePacket(_questSocketClient, scriptedDropRequest);
        // One op60 per participant: the host's under its room identity (the quest index), the guest's under its PID.
        var scriptedDropReply = new QuestListenDropItem { Opcode = 60, Pid = _questRoomMembers.QuestOf(_questSocketClient.Id) };
        scriptedDropReply.Drops.Add(new QuestListenDropItem.Drop(603056, 1));
        var guestScriptedDropReply = new QuestListenDropItem { Opcode = 60, Pid = OfflineGuestPid };
        guestScriptedDropReply.Drops.Add(new QuestListenDropItem.Drop(603056, 1));
        Check(ReadQuestSocketPacket().SequenceEqual(scriptedDropReply.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(guestScriptedDropReply.Serialize()),
            "scripted item-factory op57 rolls its exact group and sends native op60 on the Game socket");
        int scriptedItemBefore = _playerStore.GetInventory(OfflineHostPid, () => [])
            .Where(item => item.ItemId == 603056).Sum(item => item.Count);
        byte[] scriptedPickup = new byte[9];
        scriptedPickup[0] = 47;
        BinaryPrimitives.WriteInt32LittleEndian(scriptedPickup.AsSpan(1), 603056);
        BinaryPrimitives.WriteInt32LittleEndian(scriptedPickup.AsSpan(5), 1);
        Game.OnHandlePacket(_questSocketClient, scriptedPickup);
        byte[] scriptedInventoryInsert = ReadQuestSocketPacket();
        Check(scriptedInventoryInsert[0] == 82
            && ReadQuestSocketPacket().SequenceEqual(new InsertDropItemQuest
                { Answer = 0, ItemIndex = 603056, Quantity = 1 }.Serialize())
            && _playerStore.GetInventory(OfflineHostPid, () => [])
                .Where(item => item.ItemId == 603056).Sum(item => item.Count) == scriptedItemBefore + 1,
            "issued scripted pickup transfers once into persistent inventory via native op47/op50");
        Game.OnHandlePacket(_questSocketClient, scriptedPickup);
        Check(ReadQuestSocketPacket().SequenceEqual(new InsertDropItemQuest
                { Answer = 1, ItemIndex = 603056, Quantity = 1 }.Serialize())
            && _playerStore.GetInventory(OfflineHostPid, () => [])
                .Where(item => item.ItemId == 603056).Sum(item => item.Count) == scriptedItemBefore + 1,
            "replayed op47 cannot claim a scripted pickup twice");
        Game.OnHandlePacket(_questSocketClient, scriptedDropRequest);
        Check(ReadQuestSocketPacket().SequenceEqual(scriptedDropReply.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(guestScriptedDropReply.Serialize()),
            "a second scripted factory roll creates a separate pickup entitlement");
        byte[] scriptedPickupBatch = new byte[13];
        scriptedPickupBatch[0] = 48;
        BinaryPrimitives.WriteInt32LittleEndian(scriptedPickupBatch.AsSpan(1), 1);
        BinaryPrimitives.WriteInt32LittleEndian(scriptedPickupBatch.AsSpan(5), 603056);
        BinaryPrimitives.WriteInt32LittleEndian(scriptedPickupBatch.AsSpan(9), 1);
        Game.OnHandlePacket(_questSocketClient, scriptedPickupBatch);
        var batchAnswer = new InsertDropItemAtOnceQuest { Answer = 0, Items = [(603056, 1)] };
        Check(ReadQuestSocketPacket()[0] == 82
            && ReadQuestSocketPacket()[0] == 83
            && ReadQuestSocketPacket().SequenceEqual(batchAnswer.Serialize())
            && _playerStore.GetInventory(OfflineHostPid, () => [])
                .Where(item => item.ItemId == 603056).Sum(item => item.Count) == scriptedItemBefore + 2,
            "rally-end batch op48 moves only issued pickup quantities into inventory");
        Game.OnHandlePacket(_questSocketClient, scriptedPickupBatch);
        batchAnswer.Answer = 1;
        Check(ReadQuestSocketPacket().SequenceEqual(batchAnswer.Serialize())
            && _playerStore.GetInventory(OfflineHostPid, () => [])
                .Where(item => item.ItemId == 603056).Sum(item => item.Count) == scriptedItemBefore + 2,
            "replayed rally-end batch cannot mint a second copy of the pickup");
        BinaryPrimitives.WriteInt32LittleEndian(scriptedDropRequest.AsSpan(1), int.MaxValue);
        Game.OnHandlePacket(_questSocketClient, scriptedDropRequest);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestListenDropItem
            { Opcode = 60, Pid = _detachedReportQuest }.Serialize()),
            "unknown scripted drop group gets an empty op60 reply instead of fabricated loot");
        Check(_questLoot.HasGroup(81757) && !QuestLoot.IsScriptedGroup(81757)
            && QuestLoot.IsScriptedGroup(82165)
            && QuestLoot.IsScriptedGroup(82022) && QuestLoot.IsScriptedGroup(83029)
            && QuestLoot.IsScriptedGroup(83143),
            "op57 distinguishes cooked scripted groups from valid enemy-only drop groups");
        BinaryPrimitives.WriteInt32LittleEndian(scriptedDropRequest.AsSpan(1), 81757);
        Game.OnHandlePacket(_questSocketClient, scriptedDropRequest);
        Check(ReadQuestSocketPacket().SequenceEqual(new QuestListenDropItem
            { Opcode = 60, Pid = _detachedReportQuest }.Serialize()),
            "valid table group absent from scripted item factories cannot be rolled through op57");
        _questInstanceManager.ForgetHost(OfflineHostPid);
        Check(_questInstanceManager.ResolveQuestReportOwner(_questSocketClient, OfflineGuestPid, _detachedReportQuest)
                == OfflineGuestPid,
            "listen-host reports resolve a quest guest retained across expected city-link travel");
        Check(QuestManager.QuestRewardLiveRecipient(OfflineGuestPid) is null
            && ReferenceEquals(QuestManager.QuestRewardLiveRecipient(OfflineHostPid), _questSocketClient),
            "detached guest rewards persist without sending guest balances to the listen host");
        var detachedSpawnRequest = new byte[5];
        detachedSpawnRequest[0] = 61;
        BinaryPrimitives.WriteInt32LittleEndian(detachedSpawnRequest.AsSpan(1), OfflineGuestPid);
        Game.OnHandlePacket(_questSocketClient, detachedSpawnRequest);
        Check(ReadQuestSocketPacket().SequenceEqual(
                new QuestListenSpawn { Pid = OfflineGuestPid, Answer = 0 }.Serialize()),
            "detached same-instance quest guest receives successful respawn admission");
        _questRoomMembers.SetInstance(_detachedGuestClientId, OfflineHostPid + 1);
        BinaryPrimitives.WriteUInt16LittleEndian(_detachedGuestResult.AsSpan(34), 6);
        Game.OnHandlePacket(_questSocketClient, _detachedGuestResult);
        Check(_questStore.GetProgress(OfflineGuestPid, _detachedReportQuest).WeaponKills.GetValueOrDefault((byte)1) == 5,
            "op60 from a different quest instance cannot alter a detached guest's weapon kills");
        Check(_questInstanceManager.ResolveQuestReportOwner(_questSocketClient, OfflineGuestPid, _detachedReportQuest) == 0,
            "detached quest participant cannot receive reports from a different instance");
        Game.OnHandlePacket(_questSocketClient, detachedSpawnRequest);
        Check(ReadQuestSocketPacket().SequenceEqual(
                new QuestListenSpawn { Pid = OfflineGuestPid, Answer = 4 }.Serialize()),
            "detached different-instance quest guest cannot receive respawn admission");
        var cityQuestCharacter = _characterStore.Create(930821900, "CityQuestWire", 1, new Custom());
        _cityQuestWirePid = cityQuestCharacter.Pid;
        _questSocketClient.Player = _playerManager.Get(_cityQuestWirePid);
        _cityBpBefore = _playerStore.GetProgress(_cityQuestWirePid).BP;
        Check(_questRewards.Get(_cityQuestWirePid, AcquiredCityQuest).BP == 100
            && _questStore.Acquire(_cityQuestWirePid, AcquiredCityQuest),
            "city Kismet quest can be acquired before its objective callback");
    }

    private void EncryptedVendorBankTram()
    {
        _questSocketClient.Player = _playerManager.Get(_ownedA.Pid);
        using (var sellRequest = new PacketWriter())
        {
            sellRequest.Write((byte)65);
            sellRequest.Write((byte)5);
            sellRequest.Write(1);
            Game.OnHandlePacket(_questSocketClient, sellRequest.Buffer);
        }
        byte[] saleCountWire = ReadQuestSocketPacket();
        byte[] saleMoneyWire = ReadQuestSocketPacket();
        byte[] saleUndoWire = ReadQuestSocketPacket();
        byte[] saleResultWire = ReadQuestSocketPacket();
        byte undoSerial = saleUndoWire.Length > 1 ? saleUndoWire[1] : (byte)0;
        Check(saleCountWire.SequenceEqual(new UpdateItemNum { Inventory = 0, Cell = 5, Count = 1 }.Serialize())
            && saleMoneyWire.SequenceEqual(new Parameter
                { Param = Parameter.PARAMETER_MONEY, Value = 1_064 }.Serialize())
            && saleUndoWire.SequenceEqual(new UndoItemInsert
                { Serial = undoSerial, ItemId = 604221, Count = 1, Price = 64 }.Serialize())
            && saleResultWire.SequenceEqual(new SellItem { Result = 0 }.Serialize())
            && undoSerial != 0,
            "encrypted vendor sale publishes the faction-adjusted proceeds as its undo price");
        Game.OnHandlePacket(_questSocketClient, new byte[] { 177, undoSerial });
        byte[] undoResultWire = ReadQuestSocketPacket();
        byte[] undoRemoveWire = ReadQuestSocketPacket();
        byte[] undoItemWire = ReadQuestSocketPacket();
        byte[] undoMoneyWire = ReadQuestSocketPacket();
        Check(undoResultWire.SequenceEqual(new UndoItemResult { Result = 0 }.Serialize())
            && undoRemoveWire.SequenceEqual(new UndoItemRemove { Serial = undoSerial }.Serialize())
            && undoItemWire[0] == 82
            && undoMoneyWire.SequenceEqual(new Parameter
                { Param = Parameter.PARAMETER_MONEY, Value = 1_000 }.Serialize())
            && _playerStore.GetProgress(_ownedA.Pid).Money == 1_000
            && _playerStore.GetInventory(_ownedA.Pid, () => []).Count(item => item.ItemId == 604221) == 2,
            "encrypted vendor undo restores the item and charges exactly the credited proceeds");
        ResetCharacter(_ownedA.Pid);
        _playerStore.GetInventory(_ownedA.Pid, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = 0, InventoryCell = 4, ItemId = 675001, Count = 3 });
        Game.OnHandlePacket(_questSocketClient, new byte[] { 77, 4 });
        byte[] expansionRemoveWire = ReadQuestSocketPacket();
        byte[] expansionRemainingWire = ReadQuestSocketPacket();
        byte[] expansionResultWire = ReadQuestSocketPacket();
        Check(expansionRemoveWire.SequenceEqual(new RemoveItem { InventoryIndex = 0, Cell = 4 }.Serialize())
            && expansionRemainingWire.SequenceEqual(new InsertItem
                { InventoryIndex = 0, Cell = 4, ItemID = 675001, Count = 2, Durability = 100 }.Serialize())
            && expansionResultWire.SequenceEqual(new UseItem { Result = 0, UseType = 5 }.Serialize())
            && _playerStore.BankCapacity(_ownedA.Pid) == 20,
            "encrypted op77 bank expansion refreshes the remaining stacked item and capacity");
        Game.OnHandlePacket(_questSocketClient, new byte[] { 79, 4, 3 });
        byte[] bankDepositWire = ReadQuestSocketPacket();
        Check(bankDepositWire.SequenceEqual(new InsertBank
                { Result = 0, MainCell = 4, BankCell = 3 }.Serialize())
            && _playerStore.GetInventory(_ownedA.Pid, () => []).Any(item =>
                item.InventoryIndex == 5 && item.InventoryCell == 3 && item.ItemId == 675001 && item.Count == 2)
            && !_playerStore.GetInventory(_ownedA.Pid, () => []).Any(item =>
                item.InventoryIndex == 0 && item.InventoryCell == 4),
            "encrypted bank deposit moves the owned stack into its requested bank slot");
        Game.OnHandlePacket(_questSocketClient, new byte[] { 80, 3, 5 });
        byte[] bankWithdrawWire = ReadQuestSocketPacket();
        Check(bankWithdrawWire.SequenceEqual(new TakeoutBank
                { Result = 0, BankCell = 3, MainCell = 5 }.Serialize())
            && _playerStore.GetInventory(_ownedA.Pid, () => []).Any(item =>
                item.InventoryIndex == 0 && item.InventoryCell == 5 && item.ItemId == 675001 && item.Count == 2)
            && !_playerStore.GetInventory(_ownedA.Pid, () => []).Any(item =>
                item.InventoryIndex == 5 && item.InventoryCell == 3),
            "encrypted bank withdrawal restores the same stack to its requested MAIN slot");
        Game.OnHandlePacket(_questSocketClient, new byte[] { 79, 5, 20 });
        Check(ReadQuestSocketPacket().SequenceEqual(new InsertBank { Result = 1 }.Serialize())
            && _playerStore.GetInventory(_ownedA.Pid, () => []).Any(item =>
                item.InventoryIndex == 0 && item.InventoryCell == 5 && item.ItemId == 675001 && item.Count == 2),
            "encrypted bank deposit rejects a cell outside the expanded retail capacity");
        Game.OnHandlePacket(_questSocketClient, new byte[] { 84 });
        byte[] arrangedRemoveWire = ReadQuestSocketPacket();
        byte[] arrangedItemWire = ReadQuestSocketPacket();
        byte[] arrangedViewWire = ReadQuestSocketPacket();
        byte[] arrangedResultWire = ReadQuestSocketPacket();
        Check(arrangedRemoveWire.SequenceEqual(new RemoveItem { InventoryIndex = 0, Cell = 5 }.Serialize())
            && arrangedItemWire.SequenceEqual(new InsertItem
                { InventoryIndex = 0, Cell = 0, ItemID = 675001, Count = 2, Durability = 100 }.Serialize())
            && arrangedViewWire.SequenceEqual(new UpdateInventoryViews
                { View = UpdateInventoryViews.Inventory }.Serialize())
            && arrangedResultWire.SequenceEqual(new ItemArrange { Result = 0 }.Serialize())
            && _playerStore.GetInventory(_ownedA.Pid, () => []).Any(item =>
                item.InventoryIndex == 0 && item.InventoryCell == 0 && item.ItemId == 675001 && item.Count == 2),
            "encrypted item arrange compacts MAIN and publishes the exact replacement before success");
        long tramMoneyBefore = _playerStore.GetProgress(_ownedA.Pid).Money;
        Game.OnHandlePacket(_questSocketClient, new byte[] { 104, 1 });
        Check(ReadQuestSocketPacket().SequenceEqual(new TransportEnter { Answer = 0 }.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(new Parameter
                { Param = Parameter.PARAMETER_MONEY, Value = (int)(tramMoneyBefore - 100) }.Serialize()),
            "encrypted city tram boarding charges the cooked fare and acknowledges the ride");
        using (var tramMovement = new PacketWriter())
        {
            tramMovement.Write((byte)18); tramMovement.Write(1234f); tramMovement.Write(5678f);
            tramMovement.Write(90f); tramMovement.Write((short)320);
            Game.OnHandlePacket(_questSocketClient, tramMovement.Buffer);
        }
        Game.OnHandlePacket(_questSocketClient, new byte[] { 105 });
        Check(ReadQuestSocketPacket().SequenceEqual(new TransportLeave
                { Answer = 0, X = 1234, Y = 5678, Z = 90, Yaw = 320 }.Serialize())
            && _playerStore.GetProgress(_ownedA.Pid).Money == tramMoneyBefore - 100,
            "encrypted tram dismount returns the latest op18 position instead of the boarding position");
        var optionalQuestCharacter = _characterStore.Create(930822807, "OptionalWire", 1, new Custom());
        _optionalQuestPid = optionalQuestCharacter.Pid;
        _questSocketClient.Player = _playerManager.Get(_optionalQuestPid);
        _playerStore.GetInventory(_optionalQuestPid, () => []);
    }

    private void QuestHostWorldUrl()
    {
        _playerManager.SetOffline(OfflineHostPid);
        _playerManager.SetOffline(OfflineGuestPid);
        _questSocketClient.Session.ExpectedQuestTravel = false;
        _questRoomMembers.ClearQuest(OfflineGuestClientId);
        _questRoomMembers.ClearDifficulty(OfflineGuestClientId);
        _questRoomMembers.TryClearInstance(OfflineGuestClientId, out _);
        _questSocketClient.Dispose();
        _questSocketClient.SendPacket(new QuestJoin { Answer = 1 });
        Check(true, "closed Game socket rejects later quest sends without throwing");
        var hostRoom = new QuestCreate { QuestIndex = 820300 };
        Check(QuestInstanceManager.BuildQuestHostMap("Q-820300", gatewayEnabled: true) == "Q-820300?Master"
            && QuestInstanceManager.BuildQuestHostMap("Q-820300", gatewayEnabled: false) == "Q-820300",
            "quest host world URL selects gateway-only JOIN only when the gateway is enabled");
        var guestPermission = new QuestPermissionRegister { Pid = 920002, Key = _guestTravelKey };
        var hostLaunch = QuestInstanceManager.BuildQuestHostLaunch(hostRoom, _guestTravelKey, [guestPermission]);
        Check(hostLaunch.Length == 3 && ReferenceEquals(hostLaunch[0], hostRoom)
            && hostLaunch[1] is QuestPermissionRegister gatewayPermission
            && gatewayPermission.Pid == hostRoom.QuestIndex
            && gatewayPermission.Key.SequenceEqual(_guestTravelKey)
            && ReferenceEquals(hostLaunch[2], guestPermission),
            "quest host receives room creation before gateway and squad-member permissions");
        var opaqueToken = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        Check(new SessionOpaqueToken { Value = opaqueToken }.Serialize()
                .SequenceEqual(new byte[] { 5, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 })
            && new SetRelayInfo
            {
                HostPid = 0x01020304,
                RelayEndpoint = new P2PEndpoint(unchecked((int)0xAABBCCDD), 7777)
            }.Serialize().SequenceEqual(new byte[] { 206, 4, 3, 2, 1, 0xDD, 0xCC, 0xBB, 0xAA, 0x61, 0x1E })
            && new GameGuardState { State = 2 }.Serialize().SequenceEqual(new byte[] { 235, 2 }),
            "server op5/op206/op235 match the native opaque-token, relay-address, and GameGuard layouts");

        bool boundedRoundTrip = true;
        for (uint value = 0; value < 135; value++)
        {
            var boundedWriter = new BitWriter();
            boundedWriter.WriteInt(value, 135);
            var boundedReader = new BitReader(boundedWriter.ToArray(), boundedWriter.NumBits);
            boundedRoundTrip &= boundedReader.ReadInt(135) == value && !boundedReader.Overflowed;
        }
        Check(boundedRoundTrip, "UE3 value-aware SerializeInt round-trips every value below 135");
    }
}
