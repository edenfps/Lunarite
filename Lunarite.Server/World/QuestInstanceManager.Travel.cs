using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;

namespace Lunarite.World;

public sealed partial class QuestInstanceManager
{
    public static byte[] NewQuestConnectionKey() => RandomNumberGenerator.GetBytes(16);

    internal sealed class QuestTransport
    {
        public int QuestIndex;
        public byte Difficulty;
        public int HostPid;
        public List<int> Members = [];
        public List<QuestPermissionRegister> MemberPermissions = [];
        public byte[] Key = new byte[16];
        public uint HostIp;
        public uint HostPeerIp;
        public uint HostJoinIp;
        public ushort HostPublicPort = 7777;
        public ushort HostPeerPort = 7777;
    }

    // Host PID -> guests sent op52 travel whose arrival the host has not yet reported (client op52).
    internal sealed class QuestGuestArrivals
    {
        public int QuestIndex;
        public HashSet<int> Pending = [];
    }

    public static bool CanDispatchQuestTransport(QuestTransportSignal signal,
        int hostPid, int activeQuest, int transportHostPid, int transportQuest) =>
        signal is QuestTransportSignal.HostRoomLive or QuestTransportSignal.GatewayServerReady
        && hostPid > 0 && activeQuest > 0
        && transportHostPid == hostPid && transportQuest == activeQuest;

    public static bool CanConfirmSoloQuestHostReady(QuestTransportSignal signal,
        int hostPid, int activeQuest, int boundQuest, int boundInstance, int instanceHostPid) =>
        signal is QuestTransportSignal.HostRoomLive or QuestTransportSignal.GatewayServerReady
        && hostPid > 0 && activeQuest > 0 && boundQuest == activeQuest
        && boundInstance == hostPid && instanceHostPid == hostPid;

    public static uint QuestHostPeerIp(IPAddress? reportedLocalAddress, uint publicIp)
    {
        if (reportedLocalAddress == null) return publicIp;
        if (reportedLocalAddress.IsIPv4MappedToIPv6)
            reportedLocalAddress = reportedLocalAddress.MapToIPv4();
        if (reportedLocalAddress.AddressFamily != AddressFamily.InterNetwork) return publicIp;
        byte[] octets = reportedLocalAddress.GetAddressBytes();
        if (octets[0] is 0 or >= 224) return publicIp;
        if (octets[0] == 127 && (publicIp & 0xFF) != 127) return publicIp;
        return (uint)octets[0] | ((uint)octets[1] << 8)
            | ((uint)octets[2] << 16) | ((uint)octets[3] << 24);
    }

    public static uint QuestHostPublicIp(string? configuredHost, int reportedPublicAddress,
        uint observedAddress) => !string.IsNullOrWhiteSpace(configuredHost)
            ? P2PManager.PackedIp(configuredHost)
            : reportedPublicAddress != 0 ? unchecked((uint)reportedPublicAddress) : observedAddress;

    public static PeerAddrList BuildQuestHostPeerEndpoints(uint publicIp, uint peerIp,
        ushort publicPort = 7777, ushort peerPort = 7777) => new()
    {
        // Native op128 installs both the host's public and client-reported local endpoints.
        // They coincide for loopback/direct hosts, but can differ behind NAT.
        PublicEndpoint = new P2PEndpoint(unchecked((int)publicIp), publicPort),
        PeerEndpoint = new P2PEndpoint(unchecked((int)peerIp), peerPort)
    };

    public static PeerAddrList BuildQuestHostPeerEndpoints(uint hostIp) =>
        BuildQuestHostPeerEndpoints(hostIp, hostIp);

    public static IPacketSerialize[] BuildQuestGuestTravel(uint publicIp, uint peerIp,
        byte[] connectionKey, ushort publicPort = 7777, ushort peerPort = 7777) =>
        BuildQuestGuestTravel(publicIp, peerIp, publicIp, connectionKey, publicPort, peerPort);

    public static IPacketSerialize[] BuildQuestGuestTravel(uint publicIp, uint peerIp,
        uint joinIp, byte[] connectionKey, ushort publicPort = 7777, ushort peerPort = 7777) =>
    [
        BuildQuestHostPeerEndpoints(publicIp, peerIp, publicPort, peerPort),
        new QuestJoin { Answer = 0, ServerIP = joinIp, Data = connectionKey.ToArray() }
    ];

    public static IPacketSerialize[] BuildQuestGuestTravel(uint hostIp, byte[] connectionKey) =>
        BuildQuestGuestTravel(hostIp, hostIp, connectionKey);

    // Native UWorld::NotifyReceivedText checks the hosted-world URL for ?Master at JOIN.
    // That branch binds a master gateway but skips player Login/PostLogin for every connection,
    // guests included, so it is only used when a gateway will join.
    public static string BuildQuestHostMap(string mapName, bool gatewayEnabled) =>
        gatewayEnabled ? mapName + "?Master" : mapName;

    public static IPacketSerialize[] BuildQuestHostLaunch(
        QuestCreate room, byte[] connectionKey, IReadOnlyList<QuestPermissionRegister> memberPermissions) =>
    [
        room,
        new QuestPermissionRegister { Key = connectionKey, Pid = room.QuestIndex },
        .. memberPermissions
    ];

    public static IPacketSerialize[] BuildFailedQuestTransportGuestPackets(int hostPid) =>
    [
        new QuestJoin { Answer = QuestAcquire.FailLoadingRoom },
        ChatManager.BuildQuestDecisionFailureNotice(hostPid)
    ];

    public static IReadOnlyList<int> QuestCreateFailureGuestPids(
        int hostPid, IReadOnlyCollection<int> acceptedMembers) =>
        acceptedMembers.Where(pid => pid > 0 && pid != hostPid).Distinct().ToList();

    // DEDICATED_QUEST=1: op52 QuestJoin{Answer=0, ServerIP} sends the client to a separately run
    // quest server instead of having it host (native OnQuestJoin @0x1110ff00 -> sub_1119D180 ->
    // "START <ip>?seamless"). That HuxleyMMOGame.exe server, started without ?RemoteMasterServer
    // so HuxleyQuestGame makes its own gateway, must already have the map loaded on :7777.
    private void SendDedicatedQuestJoin(Client client, int selectedPid)
    {
        var ip = P2PManager.PackedIp(Environment.GetEnvironmentVariable("QUEST_SERVER_IP") ?? "127.0.0.1");
        _logger.LogWarning("[Quest] DEDICATED path: sending op52 QuestJoin -> connect to quest server 0x{0:X8}.", ip);
        client.SendPacket(new QuestJoin { Answer = 0, ServerIP = ip, Data = new byte[16] });
        _questEntryReservations.TryRemove(selectedPid, out _);
        _questEntryReservationStore.Remove(selectedPid);
    }

    // OnQuestCreate (op42) tells the client to host the instance map as a P2P listen server (UE
    // START; docs/research/QuestCreate-op42.md). Type 4 = P2P sets up the quest P2P socket so the local
    // player can join its own listen world; with type 0 no player spawns. The instance builds
    // its local player, progression and gear from this packet.
    internal QuestCreate BuildQuestCreate(int pid, QuestCatalog.Quest quest, byte difficultyLevel, byte[] connData)
    {
        var progress = _playerStore.GetProgress(pid);
        var character = _characterStore.Get(pid);
        return new QuestCreate
        {
            Answer = 0,
            Type = 4,
            UseNativeP2PBackup = Environment.GetEnvironmentVariable("QUEST_P2P_BACKUP_PATCH") == "1",
            // The client appends ?RemoteMasterServer itself. Without a gateway, ?Master would
            // send guest JOINs down the gateway-only branch and skip Login/PostLogin.
            HostMap = BuildQuestHostMap(quest.Level!, QuestGateway.Enabled),
            ConnData = connData,
            MinPlayers = quest.MinPlayers,
            Difficulty = difficultyLevel,
            QuestIndex = quest.EntryIndex,
            PlayerName = character?.Name ?? "Player",  // battle pawn name (op42 block strings)
            PlayerLevel = progress.Level,            // blob B offset 0 (battle HUD level + BP formula)
            PlayerStyle = character?.Style ?? 0,     // native CharInfo style
            PlayerRank = progress.Rank,              // blob B offset 3 (display only; not a socket gate)
            PlayerBP = (int)progress.BP,             // blob C offset 0 (battle BP total)
            PlayerLP = (int)Math.Min(int.MaxValue, progress.LP),       // native PointInfo offset 4
            PlayerMoney = (int)Math.Min(int.MaxValue, progress.Money), // m_stPlayer money
            PersonalRespawn = progress.RespawnCount, // blob E offset 0
            InvenExpandStep = progress.InvenExpandStep,
            BankExpandStep = progress.BankExpandStep,
            Character = (byte)(character?.Index ?? 6),             // species TypeIndex -> quest pawn
            Appearance = character?.Custom ?? new Custom(),        // 25-byte PostData m_stCustom
            DurativeItems = _durativeItemStore.Active(pid),         // native 20-byte op42 array
            PersonalBuffs = _personalBuffStore.Active(pid),         // native 12-byte op42 array
            Licenses = _licenseStore.Effective(pid).ToList(),       // native int op42 array
            PcRoomValid = _serverEvents.PcRoomActive,               // PostData byPCRoomValid
            SkillSlots = _playerStore.GetSkillSlots(pid),
            // Equipped gear is the only gear source in a battle quest. The boot-camp tutorial uses
            // its fixed kit (Machine Gun, Flinger, Regulator). The host's displaced stream carries no
            // supplies: on the live server a belt record left the host with no weapon.
            Items = quest.QuestType == "QuestType_Tutorial" ? ItemManager.TutorialLoadoutItems()
                : _itemManager.StartingLoadoutItems(pid).Where(item => item.InvenIndex != InventoryType.Belt).ToList()
        };
    }

    // op55 MMOPermission record for one squad guest: its PID, the room KEY and the character
    // snapshot the host's listen world builds the guest's pawn from.
    internal QuestPermissionRegister BuildQuestMemberPermission(int memberPid, byte[] connData)
    {
        var progress = _playerStore.GetProgress(memberPid);
        var character = _characterStore.Get(memberPid);
        return new QuestPermissionRegister
        {
            Key = connData,
            Pid = memberPid,
            PlayerName = character?.Name ?? "",
            Character = (byte)(character?.Index ?? 6),
            Money = (int)Math.Clamp(progress.Money, int.MinValue, int.MaxValue),
            Appearance = character?.Custom ?? new Custom(),
            Level = progress.Level,
            Style = character?.Style ?? 0,
            Rank = progress.Rank,
            BP = (int)Math.Clamp(progress.BP, int.MinValue, int.MaxValue),
            LP = (int)Math.Clamp(progress.LP, int.MinValue, int.MaxValue),
            RespawnCount = progress.RespawnCount,
            InvenExpandStep = progress.InvenExpandStep,
            BankExpandStep = progress.BankExpandStep,
            DurativeItems = _durativeItemStore.Active(memberPid),
            PersonalBuffs = _personalBuffStore.Active(memberPid),
            Licenses = _licenseStore.Effective(memberPid).ToList(),
            PcRoomValid = _serverEvents.PcRoomActive,
            SkillSlots = _playerStore.GetSkillSlots(memberPid),
            Items = _itemManager.StartingLoadoutItems(memberPid)
        };
    }

    // Where guests browse (op52, the host's UE3 listen address) and which P2P UDP endpoints op128
    // installs. The NAT-reported public UDP IP can differ from the TCP source, e.g. for same-PC
    // clients on separate -MULTIHOME loopbacks.
    private void PrepareQuestTransport(Client client, int hostPid, int questIndex, byte difficultyLevel,
        IEnumerable<int> members, List<QuestPermissionRegister> memberPermissions, byte[] connData,
        P2PNatEndpointReport? hostNatEndpoints, (uint Ip, ushort Port)? hostReportedPublic,
        ushort hostPublicPort, ushort hostPeerPort)
    {
        string? configuredHost = Environment.GetEnvironmentVariable("QUEST_HOST_IP");
        uint hostJoinIp = P2PManager.PackedIp(configuredHost ?? client.IPAddress.MapToIPv4().ToString());
        uint hostPublicIp = QuestHostPublicIp(configuredHost,
            unchecked((int)(hostReportedPublic?.Ip ?? 0)),
            hostJoinIp);
        var hostLocalAddress = client.Session.LocalAddress;
        hostLocalAddress = P2PManager.ReportedNatLocalAddress(hostNatEndpoints?.LocalAddress ?? 0) ?? hostLocalAddress;
        uint hostPeerIp = QuestHostPeerIp(hostLocalAddress, hostPublicIp);
        var memberList = members.ToList();
        _questTransports[hostPid] = new QuestTransport
        {
            QuestIndex = questIndex,
            Difficulty = difficultyLevel,
            HostPid = hostPid,
            Members = memberList,
            MemberPermissions = memberPermissions,
            Key = connData.ToArray(),
            HostIp = hostPublicIp,
            HostPeerIp = hostPeerIp,
            HostJoinIp = hostJoinIp,
            HostPublicPort = hostPublicPort,
            HostPeerPort = hostPeerPort
        };
        _logger.LogInformation(
            "[QuestTransport] prepared host PID {0}, quest {1}, members [{2}], public 0x{3:X8}:{6}, reported-local {4}:{7}, peer 0x{5:X8}.",
            hostPid, questIndex, string.Join(",", memberList), hostPublicIp,
            hostLocalAddress?.ToString() ?? "absent", hostPeerIp, hostPublicPort, hostPeerPort);
    }

    // Client op51 carries the room identity from the op42 host but the member's own MMO PID from
    // an op52 guest. Both map back to the connection's active quest.
    public static int ResolveQuestCreateAcknowledgment(
        int wireIdentity, byte ready, int selectedPid, int activeQuest) =>
        ready == 1 && activeQuest > 0
            && (wireIdentity == activeQuest || wireIdentity == selectedPid)
                ? activeQuest
                : 0;

    public bool ConfirmQuestHostReady(int hostPid, int activeQuest, QuestTransportSignal signal)
    {
        bool partyTransport = _questTransports.TryGetValue(hostPid, out var transport);
        if (partyTransport)
        {
            if (!CanDispatchQuestTransport(signal, hostPid, activeQuest,
                    transport!.HostPid, transport.QuestIndex))
            {
                _logger.LogWarning("[QuestTransport] rejected host-ready {0}: PID {1}, quest {2}, prepared PID {3}, quest {4}.",
                    signal, hostPid, activeQuest, transport.HostPid, transport.QuestIndex);
                return false;
            }
            _logger.LogInformation("[QuestTransport] host-ready {0}: PID {1}, quest {2}, members [{3}].",
                signal, hostPid, activeQuest, string.Join(",", transport.Members));
        }
        else
        {
            int boundClientId = Game.TryGetClient(hostPid, out var onlineHost)
                ? onlineHost.Id : _questRoomMembers.DetachedClientOf(hostPid);
            int boundQuest = _questRoomMembers.QuestOf(boundClientId);
            int boundInstance = _questRoomMembers.InstanceOf(boundClientId);
            int instanceHostPid = _questInstanceHosts.GetValueOrDefault(hostPid);
            if (!CanConfirmSoloQuestHostReady(signal, hostPid, activeQuest,
                    boundQuest, boundInstance, instanceHostPid)) return false;
        }
        if (QuestInstanceManager.ShouldCommitQuestEntryReservation(signal, true))
        {
            // The host has arrived, so its entry items are spent. Guests' items stay reserved
            // until the host reports each of them logged in (client op52) and are refunded
            // if they never arrive.
            var guests = partyTransport ? transport!.Members.Where(x => x != hostPid).Distinct().ToList() : [];
            if (guests.Count > 0 && _questEntryReservations.TryGetValue(hostPid, out var held))
            {
                lock (held) held.Members = held.Members.Where(guests.Contains).ToList();
                if (held.Members.Count > 0)
                    _questEntryReservationStore.Put(hostPid, held.Members, held.Items);
            }
            if (guests.Count == 0 || !_questEntryReservations.TryGetValue(hostPid, out var kept)
                || kept.Members.Count == 0)
            {
                _questEntryReservations.TryRemove(hostPid, out _);
                _questEntryReservationStore.Remove(hostPid);
            }
        }
        if (Game.TryGetClient(hostPid, out var hostClient))
            hostClient.Session.ExpectedQuestTravel = false;
        if (partyTransport) DispatchQuestMembers(hostPid, activeQuest);
        return true;
    }

    // The client expires each MMOPermission entry after 30 seconds, and host map loading can take
    // that long. Native InsertVerify replaces an existing PID's record with a fresh GetTickCount
    // timestamp (MMOPermission.cpp), so re-sending op55 renews it until room-live.
    internal async System.Threading.Tasks.Task RefreshQuestMemberPermissionsUntilReady(
        int hostPid, QuestTransport transport, TimeSpan interval)
    {
        try
        {
            while (true)
            {
                await System.Threading.Tasks.Task.Delay(interval);
                if (!_questTransports.TryGetValue(hostPid, out var current)
                    || !ReferenceEquals(current, transport)) return;
                if (!Game.TryGetClient(hostPid, out var hostClient)) return;
                QuestPermissionRegister[] permissions;
                lock (transport) permissions = transport.MemberPermissions.ToArray();
                foreach (var permission in permissions)
                    hostClient.SendPacket(permission);
                _logger.LogInformation("[QuestTransport] refreshed guest permission PIDs [{0}] for host PID {1}.",
                    string.Join(",", transport.MemberPermissions.Select(x => x.Pid)), hostPid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[QuestTransport] permission refresh stopped for host PID {0}: {1}",
                hostPid, ex.Message);
        }
    }

    private void DispatchQuestMembers(int hostPid, int activeQuest)
    {
        if (!_questTransports.TryRemove(hostPid, out var transport)
            || !CanDispatchQuestTransport(QuestTransportSignal.HostRoomLive,
                hostPid, activeQuest, transport.HostPid, transport.QuestIndex))
            return;

        // Renew the guests' 30-second MMOPermission window at dispatch, since the loading-phase
        // refresh stops once this transport is removed.
        if (Game.TryGetClient(hostPid, out var hostClient))
            foreach (var permission in transport.MemberPermissions)
                hostClient.SendPacket(permission);

        var arrivals = new QuestGuestArrivals { QuestIndex = activeQuest };
        foreach (int memberPid in transport.Members.Where(x => x != hostPid))
        {
            if (!Game.TryGetClient(memberPid, out var memberClient))
            {
                _logger.LogWarning("[QuestTransport] guest PID {0} offline before dispatch from host PID {1}, quest {2}.",
                    memberPid, hostPid, activeQuest);
                RefundQuestGuestEntry(hostPid, memberPid, "offline before dispatch");
                continue;
            }
            arrivals.Pending.Add(memberPid);
            _cityManager.LeaveCurrentPresence(memberClient, memberPid);
            _questRoomMembers.SetQuest(memberClient.Id, activeQuest);
            _questRoomMembers.SetInstance(memberClient.Id, hostPid);
            _questRoomMembers.SetDifficulty(memberClient.Id, transport.Difficulty);
            // The guest's city link stays up in the host's world, so it is not armed for quest
            // travel: a close is a crash or quit and leaves the squad and the instance.
            // Native OnQuestJoin only browses to the IP. ProcPeerAddrList (op128) queues a hole
            // probe to the endpoints before travel.
            var travel = BuildQuestGuestTravel(P2PManager.HostIpFor(transport.HostIp, memberClient),
                P2PManager.HostIpFor(transport.HostPeerIp, memberClient), P2PManager.HostIpFor(transport.HostJoinIp, memberClient),
                transport.Key, transport.HostPublicPort, transport.HostPeerPort);
            // The host needs the guest's endpoints too, as the Simulation owner gets them: its
            // listen socket hole-punches the peers op128 put on its ping list when the map binds
            // (InitListen). Without that the host never sends first, and a guest behind the
            // internet (not a VPN or LAN) is dropped by the host's NAT (live 2026-11-04).
            var guest = memberClient;
            void Probe()
            {
                if (Game.TryGetClient(hostPid, out var listenHost))
                    listenHost.SendPacket(P2PManager.HostEndpointsFor(P2PManager.BuildClientPeerEndpoints(guest, null,
                        P2PManager.PackedIp(guest.IPAddress.MapToIPv4().ToString())), listenHost));
                guest.SendPacket(travel[0]);
            }
            memberClient.Session.HolePunchAcknowledged = false;
            Probe();
            _logger.LogInformation(
                "[QuestTransport] sent guest PID {0} op128 for host PID {1}, quest {2}, public 0x{3:X8}, peer 0x{4:X8}.",
                memberPid, hostPid, activeQuest, transport.HostIp, transport.HostPeerIp);
            _ = _p2pManager.SendAfterHolePunch(memberClient, memberPid, hostPid, travel[1], "op52", Probe);
        }
        if (arrivals.Pending.Count == 0) return;
        _questGuestArrivals[hostPid] = arrivals;
        _ = ExpireQuestGuestArrivals(hostPid, arrivals, QuestGuestArrivalTimeout());
    }

    private static TimeSpan QuestGuestArrivalTimeout() => TimeSpan.FromSeconds(
        int.TryParse(Environment.GetEnvironmentVariable("QUEST_GUEST_ARRIVAL_TIMEOUT_SECONDS"), out int seconds)
            && seconds > 0 ? seconds : 120);

    // Refunds guests the host has not reported (client op52) within the window.
    private async System.Threading.Tasks.Task ExpireQuestGuestArrivals(
        int hostPid, QuestGuestArrivals arrivals, TimeSpan timeout)
    {
        await System.Threading.Tasks.Task.Delay(timeout);
        if (!_questGuestArrivals.TryGetValue(hostPid, out var current) || !ReferenceEquals(current, arrivals)) return;
        _questGuestArrivals.TryRemove(hostPid, out _);
        int[] missing;
        lock (arrivals) missing = arrivals.Pending.ToArray();
        foreach (int memberPid in missing)
        {
            _logger.LogWarning("[QuestTransport] guest PID {0} never reached host PID {1}, quest {2}, within {3}s.",
                memberPid, hostPid, arrivals.QuestIndex, (int)timeout.TotalSeconds);
            RefundQuestGuestEntry(hostPid, memberPid, "never arrived");
        }
    }

    // Returns one guest's held entry items and drops it from the host's reservation.
    private void RefundQuestGuestEntry(int hostPid, int memberPid, string reason)
    {
        if (!_questEntryReservations.TryGetValue(hostPid, out var reservation)) return;
        lock (reservation)
            if (!reservation.Members.Remove(memberPid)) return;
        var items = reservation.Items;
        bool refunded = false;
        _multiStoreTransaction.Run(() =>
        {
            SaveQuestEntryReservation(hostPid, reservation);
            if (!_playerStore.TryExchangeQuestItems(memberPid, [], items, out var restored, out var changedCells))
            {
                _logger.LogError("[QuestTransport] could not refund entry items to guest PID {0} (host {1}).",
                    memberPid, hostPid);
                return;
            }
            refunded = true;
            _itemManager.SynchronizeQuestPickupObjectives(memberPid, items.Select(item => item.ItemId));
            if (Game.TryGetClient(memberPid, out var memberClient))
                QuestManager.SendQuestInventoryChanges(memberClient, changedCells, restored);
        });
        if (refunded)
            _logger.LogInformation("[QuestTransport] refunded entry items to guest PID {0} of host PID {1} ({2}).",
                memberPid, hostPid, reason);
    }

    // A dispatched guest's link closed before the host reported its arrival. Only that guest's
    // held entry items are returned; the room and the other guests' reservations stay.
    internal bool AbandonDispatchedGuest(int memberPid)
    {
        foreach (var (hostPid, arrivals) in _questGuestArrivals)
        {
            lock (arrivals)
                if (!arrivals.Pending.Remove(memberPid)) continue;
            RefundQuestGuestEntry(hostPid, memberPid, "disconnected before arrival");
            return true;
        }
        return false;
    }

    // The guest reached the host's world, so its held entry items are spent.
    internal void CommitQuestGuestEntry(int hostPid, int memberPid)
    {
        if (!_questEntryReservations.TryGetValue(hostPid, out var reservation)) return;
        lock (reservation)
            if (!reservation.Members.Remove(memberPid)) return;
        SaveQuestEntryReservation(hostPid, reservation);
    }

    internal void SetTransport(QuestTransport transport) => _questTransports[transport.HostPid] = transport;
}
