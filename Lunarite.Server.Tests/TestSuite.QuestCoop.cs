using System.Buffers.Binary;
using System.Net;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

// Co-op mission links: the P2P host and guests keep their city Game link, so a close is a crash
// or quit, never travel.
internal sealed partial class TestSuite
{
    private sealed class CoopLink(Client client, System.Net.Sockets.TcpClient peer) : IDisposable
    {
        public Client Client = client;
        public System.Net.Sockets.TcpClient Peer = peer;
        public void Dispose()
        {
            Client.Player = null;
            Client.Dispose();
            Peer.Dispose();
        }
    }

    private CoopLink OpenCoopLink(int id, string localAddress)
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var peer = new System.Net.Sockets.TcpClient(new IPEndPoint(IPAddress.Parse(localAddress), 0));
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = listener.AcceptTcpClient();
        listener.Stop();
        var link = new CoopLink(new Client(id, new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), accepted), peer);
        peer.ReceiveTimeout = 3000;
        _ = ReceiveCoop(link); // GameVersion
        return link;
    }

    private static byte[] ReceiveCoop(CoopLink link)
    {
        var stream = link.Peer.GetStream();
        Span<byte> header = stackalloc byte[2];
        stream.ReadExactly(header);
        var payload = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header) - 2];
        stream.ReadExactly(payload);
        for (int i = 0; i < payload.Length; i++) payload[i] = link.Client.DecryptTable[0, payload[i]];
        return payload;
    }

    // Reads every packet already sent, until the link stays quiet.
    private static List<byte[]> DrainCoop(CoopLink link)
    {
        var packets = new List<byte[]>();
        link.Peer.ReceiveTimeout = 500;
        try { while (true) packets.Add(ReceiveCoop(link)); }
        catch (IOException) { }
        link.Peer.ReceiveTimeout = 3000;
        return packets;
    }

    // Two selected squadmates whose leader has the two-member mission 846000 open (op42 + op55
    // sent, op199 not yet).
    private (CoopLink Host, CoopLink Guest, int HostPid, int GuestPid) OpenCoopRoom(int account, string name)
    {
        const int coopQuestIndex = 846000;
        var hostCharacter = _characterStore.Create(account, name + "H", 1, new Custom());
        var guestCharacter = _characterStore.Create(account + 1, name + "G", 1, new Custom());
        foreach (int memberPid in new[] { hostCharacter.Pid, guestCharacter.Pid })
        {
            ResetCharacter(memberPid);
            var inventory = _playerStore.GetInventory(memberPid, () => []);
            inventory.Add(new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 604221, Durability = 100 });
            inventory.Add(new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 200299, Durability = 100 });
        }
        var host = OpenCoopLink(account, "127.0.0.2");
        var guest = OpenCoopLink(account + 1, "127.0.0.3");
        _playerManager.SetOnline(hostCharacter.Pid, host.Client);
        _playerManager.SetOnline(guestCharacter.Pid, guest.Client);
        host.Client.Player = _playerManager.Get(hostCharacter.Pid);
        guest.Client.Player = _playerManager.Get(guestCharacter.Pid);
        host.Client.Session.AccountId = account;
        guest.Client.Session.AccountId = account + 1;
        _squadManager.SetSquad(hostCharacter.Pid, hostCharacter.Pid, [hostCharacter.Pid, guestCharacter.Pid]);

        using var check = new PacketWriter();
        check.Write((byte)33); check.Write(coopQuestIndex); check.Write((byte)0);
        Game.OnHandlePacket(host.Client, check.Buffer);
        _ = ReceiveCoop(guest); // op44 decision
        using var accept = new PacketWriter();
        accept.Write((byte)34); accept.Write(coopQuestIndex); accept.Write((byte)0);
        Game.OnHandlePacket(guest.Client, accept.Buffer);
        var launch = new[] { ReceiveCoop(host), ReceiveCoop(host), ReceiveCoop(host), ReceiveCoop(host) };
        Check(launch[1][0] == 42 && launch[3][0] == 55 && _questInstanceManager.HasTransport(hostCharacter.Pid),
            "co-op fixture opens the room with op42 and the guest's op55");
        return (host, guest, hostCharacter.Pid, guestCharacter.Pid);
    }

    private void CloseCoopRoom(CoopLink host, CoopLink guest, int hostPid, int guestPid)
    {
        DisconnectHandler.HandleDisconnect(host.Client);
        DisconnectHandler.HandleDisconnect(guest.Client);
        _squadManager.RemoveSquad(hostPid);
        _playerManager.SetOffline(hostPid);
        _playerManager.SetOffline(guestPid);
        host.Dispose();
        guest.Dispose();
    }

    private void CoopHostCrashWhileLoading()
    {
        string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        var (host, guest, hostPid, guestPid) = OpenCoopRoom(939847001, "CoopLoad");
        try
        {
            Check(!host.Client.Session.ExpectedQuestTravel,
                "op42 does not arm quest travel: the listen host keeps its city link while loading");
            DisconnectHandler.HandleDisconnect(host.Client);
            var guestPackets = DrainCoop(guest);
            var failure = QuestInstanceManager.BuildFailedQuestTransportGuestPackets(hostPid)
                .Select(packet => packet.Serialize()).ToList();
            int failureAt = guestPackets.FindIndex(packet => packet.SequenceEqual(failure[0]));
            Check(failureAt >= 0 && failureAt + 1 < guestPackets.Count
                && guestPackets[failureAt + 1].SequenceEqual(failure[1])
                && !_questInstanceManager.HasTransport(hostPid)
                && !_questInstanceManager.IsInstanceOpen(hostPid)
                && _questRoomMembers.DetachedClientOf(hostPid) == 0,
                "a host crash before op199 abandons the room and tells the waiting guest");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            CloseCoopRoom(host, guest, hostPid, guestPid);
        }
    }

    private void CoopGuestCrashInMission()
    {
        const int otherGuestPid = 939847999;
        string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        var (host, guest, hostPid, guestPid) = OpenCoopRoom(939847003, "CoopMid");
        try
        {
            using var ready = new PacketWriter();
            ready.Write((byte)199); ready.Write(846000);
            Game.OnHandlePacket(host.Client, ready.Buffer);
            _ = ReceiveCoop(host);  // renewed op55
            _ = ReceiveCoop(guest); // op128
            _ = ReceiveCoop(guest); // op125
            Check(ReceiveCoop(guest)[0] == 52 && !guest.Client.Session.ExpectedQuestTravel
                && _questRoomMembers.InstanceOf(guest.Client.Id) == hostPid,
                "op52 dispatch binds the guest to the room without arming quest travel");

            // Another guest is still on its way, and both hold entry items under the host.
            _questInstanceManager.SetEntryReservation(hostPid, new QuestInstanceManager.QuestEntryReservation
            {
                Members = [guestPid, otherGuestPid], Items = [(604222, 1)]
            });
            Check(_questInstanceManager.TryGetGuestArrivals(hostPid, out var arrivals),
                "dispatch tracks the guest's arrival");
            lock (arrivals) arrivals.Pending.Add(otherGuestPid);

            DisconnectHandler.HandleDisconnect(guest.Client);
            bool refunded = _playerStore.GetInventory(guestPid, () => [])
                .Any(item => item.InventoryIndex == InventoryType.Main && item.ItemId == 604222);
            bool otherKept = _questInstanceManager.TryTakeQuestEntryReservationForMember(otherGuestPid,
                    out int reservationHost, out var kept)
                && reservationHost == hostPid && kept!.Members.SequenceEqual([otherGuestPid]);
            bool pendingKept;
            lock (arrivals) pendingKept = !arrivals.Pending.Contains(guestPid) && arrivals.Pending.Contains(otherGuestPid);
            Check(refunded && otherKept && pendingKept,
                "a dispatched guest's crash refunds only its own held entry items");
            Check(_questRoomMembers.DetachedClientOf(guestPid) == 0
                && _questRoomMembers.InstanceOf(guest.Client.Id) == 0
                && !_squadManager.SquadOf(hostPid).Members.Contains(guestPid),
                "a guest crash mid-mission leaves the instance and the squad instead of lingering as travel");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            _questEntryReservationStore.Remove(hostPid);
            _questInstanceManager.ForgetGuestArrivals(hostPid);
            CloseCoopRoom(host, guest, hostPid, guestPid);
        }
    }

    private void CoopPcRoomAndBackupLevelBp()
    {
        // One 32-byte GenericItem follows the seven inventory counts; before them come the
        // byPCRoomValid byte and the 4-byte quest backup (type, BackupLevelBP, item pairs).
        var item = new QuestCreate.Item { ItemID = 200499, Count = 1, InvenIndex = 4, Cell = 4 };
        var guestPermission = new QuestPermissionRegister
        {
            Pid = 7, BP = 70000, PcRoomValid = true, Licenses = [5], Items = [item]
        }.Serialize();
        int guestItem = guestPermission.Length - 32;
        Check(BinaryPrimitives.ReadInt32LittleEndian(guestPermission.AsSpan(guestItem - 16)) == 5
            && guestPermission[guestItem - 12] == 1
            && guestPermission[guestItem - 11] == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(guestPermission.AsSpan(guestItem - 10)) == 0,
            "op55 sends byPCRoomValid after the licenses and BackupLevelBP 0, not the account BP");
        var plainPermission = new QuestPermissionRegister { Pid = 7, BP = 70000, Items = [item] }.Serialize();
        Check(plainPermission[plainPermission.Length - 32 - 12] == 0,
            "op55 byPCRoomValid stays 0 outside a PC-room event");

        var hostCreate = new QuestCreate
        {
            Answer = 0, Type = 1, HostMap = "Q-SP_Begin", PlayerBP = 70000, PcRoomValid = true, Items = [item]
        }.Serialize();
        int hostItem = hostCreate.Length - 32;
        Check(hostCreate[hostItem - 12] == 1
            && hostCreate[hostItem - 11] == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(hostCreate.AsSpan(hostItem - 10)) == 0,
            "op42 sends byPCRoomValid and BackupLevelBP 0 for a fresh room");

        var quest = _questCatalog.Get(846000)!;
        bool previous = _serverEvents.PcRoomActive;
        try
        {
            _serverEvents.SetPcRoom(true);
            bool eventOn = _questInstanceManager.BuildQuestCreate(QuestPid, quest, 0, new byte[16]).PcRoomValid
                && _questInstanceManager.BuildQuestMemberPermission(QuestPid, new byte[16]).PcRoomValid;
            _serverEvents.SetPcRoom(false);
            bool eventOff = !_questInstanceManager.BuildQuestCreate(QuestPid, quest, 0, new byte[16]).PcRoomValid
                && !_questInstanceManager.BuildQuestMemberPermission(QuestPid, new byte[16]).PcRoomValid;
            Check(eventOn && eventOff,
                "op42 and the guests' op55 carry the PC-room event to the mission's bonus");
        }
        finally
        {
            _serverEvents.SetPcRoom(previous);
        }
    }
}
