using System.Buffers.Binary;
using System.Net;
using System.Reflection;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Storage;
using Lunarite.World;
using Microsoft.Extensions.Logging;

internal sealed partial class TestSuite
{
    private (Client Client, System.Net.Sockets.TcpClient Peer) OpenSimulationLink(int pid,
        List<(Client Client, System.Net.Sockets.TcpClient Peer)> links)
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var peer = new System.Net.Sockets.TcpClient();
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = listener.AcceptTcpClient();
        listener.Stop();
        var gameClient = new Client(pid, new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), accepted);
        peer.ReceiveTimeout = 1000;
        gameClient.Player = _playerManager.Get(pid);
        _playerManager.SetOnline(pid, gameClient);
        var link = (gameClient, peer);
        links.Add(link);
        return link;
    }

    private static byte[]? ReceiveSimulationOp((Client Client, System.Net.Sockets.TcpClient Peer) link, byte opcode)
    {
        var stream = link.Peer.GetStream();
        try
        {
            while (true)
            {
                var header = new byte[2];
                stream.ReadExactly(header);
                var payload = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header) - 2];
                stream.ReadExactly(payload);
                for (int i = 0; i < payload.Length; i++) payload[i] = link.Client.DecryptTable[0, payload[i]];
                if (payload.Length != 0 && payload[0] == opcode) return payload;
            }
        }
        catch (IOException) { return null; }
    }

    private static void ReportSimulationResult((Client Client, System.Net.Sockets.TcpClient Peer) reporter,
        int pid, int bp, ushort qt)
    {
        var wire = new byte[4 + 1 + 1 + 27 + 12 + 48 + 104];
        BinaryPrimitives.WriteInt32LittleEndian(wire, pid);
        wire[4] = 1; wire[5] = 9;
        int battle = 6 + 27 + 12;
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(battle), pid);
        wire[battle + 5] = 5;
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(battle + 30), bp);
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(battle + 34), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(battle + 46), qt);
        var reader = new PacketReader(wire);
        BattleResultHandler.HandlePacket(reporter.Client, ref reader);
    }

    private int SimulationMedals(int pid) => _playerStore.GetInventory(pid, () => [])
        .Where(x => x.ItemId == BattleManager.BattleMedalItem).Sum(x => x.Count);

    // Plays one match that the server measures at `seconds` (midMatch runs while it is under way)
    // and has the host report the guest with an inflated BP and QT.
    private (long Bp, long Qt, int Medals) PlaySimulationMatch((Client Client, System.Net.Sockets.TcpClient Peer) host,
        (Client Client, System.Net.Sockets.TcpClient Peer) guest, int guestPid, int seconds, Action? midMatch = null)
    {
        var transports = (System.Collections.IDictionary)typeof(SimulationManager)
            .GetField("_simulationTransports", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_simulationManager)!;
        _simulationManager.Ready(guest.Client);
        _simulationManager.Ready(host.Client);
        ReceiveSimulationOp(host, 143);
        _simulationManager.StartMatch(host.Client);
        if (transports[Game.PidOf(host.Client)] is SimulationManager.SimulationTransport transport)
            transport.StartedAtUtc = DateTime.UtcNow.AddSeconds(-seconds);
        midMatch?.Invoke();
        _simulationManager.BattleResultSecret(host.Client);
        var before = _playerStore.GetProgress(guestPid);
        long bp = before.BP, qt = before.QT;
        int medals = SimulationMedals(guestPid);
        ReportSimulationResult(host, guestPid, 100_000, ushort.MaxValue);
        var after = _playerStore.GetProgress(guestPid);
        return (after.BP - bp, after.QT - qt, SimulationMedals(guestPid) - medals);
    }

    // Each class channel takes its class and below, Free takes everyone, and rooms stay in their channel.
    private void SimulationChannels()
    {
        Check(SimulationManager.IsSimulationChannelOpen(0, 0) && !SimulationManager.IsSimulationChannelOpen(0, 1)
            && SimulationManager.IsSimulationChannelOpen(1, 0) && !SimulationManager.IsSimulationChannelOpen(2, 3)
            && SimulationManager.IsSimulationChannelOpen(3, 3) && SimulationManager.IsSimulationChannelOpen(4, 3)
            && !SimulationManager.IsSimulationChannelOpen(5, 0),
            "Skirmish class channels refuse higher classes and the Free channel takes everyone");
        var links = new List<(Client Client, System.Net.Sockets.TcpClient Peer)>();
        var pids = new List<int>();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                int pid = _characterStore.Create(959500000 + i, $"SimChan{i}", 1, new Custom()).Pid;
                ResetCharacter(pid);
                pids.Add(pid);
            }
            var owner = OpenSimulationLink(pids[0], links);
            var other = OpenSimulationLink(pids[1], links);
            _simulationManager.JoinChannel(owner.Client, pids[0], 4);
            _simulationManager.JoinChannel(other.Client, pids[1], 1);
            _simulationManager.CreateRoom(owner.Client, "Free", 2, 102, 5, 8, 0, 1, 3, "");
            byte roomId = ReceiveSimulationOp(owner, 131)![2];
            other.Peer.ReceiveTimeout = 300;
            _simulationManager.JoinRoom(other.Client, roomId, "");
            byte[]? refused = ReceiveSimulationOp(other, 132);
            _simulationManager.JoinChannel(other.Client, pids[1], 4);
            _simulationManager.JoinRoom(other.Client, roomId, "");
            Check(refused is [132, SimulationAnswer.NoRoom, ..] && ReceiveSimulationOp(other, 132) is [132, 0, ..]
                && _simulationManager.SimulationChannelPlayers(4) == 2,
                "a Skirmish room is joined only from its own channel");
        }
        finally
        {
            foreach (int pid in pids) _simulationManager.LeaveSimulationCenter(pid);
            foreach (var link in links)
            {
                _playerManager.SetOffline(Game.PidOf(link.Client));
                link.Client.Dispose();
                link.Peer.Dispose();
            }
        }
    }

    // A Skirmish guest with no punched path joins through the relay, and keeps it until op106 sends
    // it back after the scoreboard. A guest that left during the punch gets no op156.
    private void SimulationGuestRelay()
    {
        var links = new List<(Client Client, System.Net.Sockets.TcpClient Peer)>();
        var pids = new List<int>();
        string? forceRelay = Environment.GetEnvironmentVariable("QUEST_P2P_FORCE_RELAY");
        string? punchWait = Environment.GetEnvironmentVariable("QUEST_HOLEPUNCH_WAIT_MS");
        var natService = _p2pManager.NatTestService;
        using var relayService = new P2PNatTestService(_loggerFactory.CreateLogger<P2PNatTestService>());
        try
        {
            _p2pManager.NatTestService = relayService;
            Environment.SetEnvironmentVariable("QUEST_P2P_FORCE_RELAY", "1");
            for (int i = 0; i < 2; i++)
            {
                int pid = _characterStore.Create(959490000 + i, $"SimRelay{i}", 1, new Custom()).Pid;
                ResetCharacter(pid);
                pids.Add(pid);
            }
            var host = OpenSimulationLink(pids[0], links);
            var guest = OpenSimulationLink(pids[1], links);
            guest.Client.Session.NatEndpoint = new P2PNatEndpointReport { NatType = 3, PublicPort = 7777 };
            _simulationManager.CreateRoom(host.Client, "Relay", 2, 102, 5, 8, 0, 1, 3, "");
            byte roomId = ReceiveSimulationOp(host, 131)![2];
            _simulationManager.JoinRoom(guest.Client, roomId, "");
            _simulationManager.Ready(guest.Client);
            _simulationManager.Ready(host.Client);
            ReceiveSimulationOp(host, 143);
            _simulationManager.SimulationPermissionAck(host.Client, pids[1], 1);
            byte[]? relayInfo = ReceiveSimulationOp(guest, 206);
            byte[]? relayOn = ReceiveSimulationOp(guest, 125);
            byte[]? join = ReceiveSimulationOp(guest, 156);
            Check(relayInfo != null && relayOn is [125, 0, 1] && join != null
                && guest.Client.Session.RelayHostPid == pids[0],
                "a Skirmish guest that cannot punch through joins its host through the relay");
            _simulationManager.StartMatch(host.Client);
            _simulationManager.BattleResultSecret(host.Client);
            int relayedAfterResults = guest.Client.Session.RelayHostPid;
            _simulationManager.AbortHostedMatch(pids[1]);
            Check(relayedAfterResults == pids[0] && guest.Client.Session.RelayHostPid == 0
                && ReceiveSimulationOp(guest, 125) is [125, 1, 1],
                "a Skirmish relay outlasts the scoreboard and ends when the guest leaves with op106");

            Environment.SetEnvironmentVariable("QUEST_P2P_FORCE_RELAY", null);
            Environment.SetEnvironmentVariable("QUEST_HOLEPUNCH_WAIT_MS", "300");
            _simulationManager.Ready(guest.Client);
            _simulationManager.Ready(host.Client);
            ReceiveSimulationOp(host, 143);
            _simulationManager.SimulationPermissionAck(host.Client, pids[1], 1);
            _simulationManager.LeaveRoom(pids[1]);
            guest.Peer.ReceiveTimeout = 1600;
            Check(ReceiveSimulationOp(guest, 156) == null,
                "a guest that leaves the room while hole punching gets no Skirmish join");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_P2P_FORCE_RELAY", forceRelay);
            Environment.SetEnvironmentVariable("QUEST_HOLEPUNCH_WAIT_MS", punchWait);
            _p2pManager.NatTestService = natService;
            foreach (int pid in pids) _simulationManager.LeaveSimulationCenter(pid);
            foreach (var link in links)
            {
                _playerManager.SetOffline(Game.PidOf(link.Client));
                link.Client.Dispose();
                link.Peer.Dispose();
            }
        }
    }

    // Simulation rooms only offer the retail ActiveGameTypes and never start alone; op114 credits
    // at most BattleMaxBPLimit and the time-bound QT, Skirmish medals need a full-length match,
    // a match of a second pays nothing, and identical results from two matches are both applied.
    private void SimulationRewardLimits()
    {
        var links = new List<(Client Client, System.Net.Sockets.TcpClient Peer)>();
        var pids = new List<int>();
        (Client Client, System.Net.Sockets.TcpClient Peer) OpenLink(int pid) => OpenSimulationLink(pid, links);
        static byte[]? ReceiveOp((Client Client, System.Net.Sockets.TcpClient Peer) link, byte opcode) =>
            ReceiveSimulationOp(link, opcode);
        (long Bp, long Qt, int Medals) PlayMatch((Client Client, System.Net.Sockets.TcpClient Peer) host,
            (Client Client, System.Net.Sockets.TcpClient Peer) guest, int guestPid, bool fullTime) =>
            PlaySimulationMatch(host, guest, guestPid, fullTime ? 8 * 60 : 1);
        bool pcRoom = _serverEvents.PcRoomActive;
        try
        {
            _serverEvents.SetPcRoom(false);
            for (int i = 0; i < 3; i++)
            {
                int pid = _characterStore.Create(959480000 + i, $"SimLimit{i}", 1, new Custom()).Pid;
                ResetCharacter(pid);
                pids.Add(pid);
            }
            var solo = OpenLink(pids[0]);
            _simulationManager.CreateRoom(solo.Client, "Solo", 2, 102, 6, 8, 0, 1, 3, "");
            byte[]? otherType = ReceiveOp(solo, 131);
            _simulationManager.CreateRoom(solo.Client, "Solo", 2, 102, 5, 8, 0, 1, 3, "");
            byte[]? created = ReceiveOp(solo, 131);
            _simulationManager.ChangeRoomInfo(solo.Client, 102, 6, 0, 8, 2, 3);
            byte[]? changed = ReceiveOp(solo, 135);
            _simulationManager.Ready(solo.Client);
            byte[]? soloStart = ReceiveOp(solo, 143);
            Check(otherType is [131, SimulationAnswer.Error, ..] && created is [131, 0, ..]
                && changed is [135, SimulationAnswer.Error]
                && soloStart is [143, SimulationAnswer.PlayerProportion]
                && !SimulationManager.CheckStartCondition(6, 2, new byte[] { 1, 3 }),
                "Simulation rooms take only the room menu's game types and a lone owner cannot start a match");
            Check(SimulationManager.IsSimulationGameType(3) && SimulationManager.IsSimulationGoal(3, 100, 12)
                && SimulationManager.CheckStartCondition(3, 2, new byte[] { 1, 3 })
                && _battleZoneCatalog.SupportsRoom(171, 8) && _battleZoneCatalog.SupportsRoom(180, 8)
                && _battleZoneCatalog.SupportsRoom(183, 8) && _battleZoneCatalog.SupportsRoom(174, 8)
                && _simulationMapOverrides.LevelFor(174) == "BZC-001-Earthquake"
                && _simulationMapOverrides.LevelFor(109) == "BZC-008-LargeQuake"
                && _simulationMapOverrides.LevelFor(191) == "BZT-Test-CuttleFish"
                && _simulationMapOverrides.LevelFor(180) == "BZC-007-Docks"
                && _simulationMapOverrides.LevelFor(183) == "BZP-005-Harbor",
                "Skirmish offers Pickup and every map with geometry; BZC-014-Slums's row hosts Earthquake instead");
            _simulationManager.LeaveRoom(pids[0]);

            var host = OpenLink(pids[1]);
            var guest = OpenLink(pids[2]);
            _simulationManager.CreateRoom(host.Client, "Limits", 2, 102, 5, 8, 0, 1, 3, "");
            byte roomId = ReceiveOp(host, 131)![2];
            _simulationManager.JoinRoom(guest.Client, roomId, "");
            int bpLimit = SimulationManager.SimulationBPLimit(_playerStore.GetProgress(pids[2]).Level, false, 8 * 60, 8 * 60);
            var first = PlayMatch(host, guest, pids[2], fullTime: true);
            Check(first.Bp == bpLimit && bpLimit == 1000
                && first.Qt > 0 && first.Qt <= SimulationManager.SimulationQTLimit(8 * 60 + 5, false)
                && first.Qt < ushort.MaxValue && first.Medals > 0,
                "Skirmish op114 credits at most BattleMaxBPLimit and the play-time QT ceiling");
            bpLimit = SimulationManager.SimulationBPLimit(_playerStore.GetProgress(pids[2]).Level, false, 8 * 60, 8 * 60);
            var second = PlayMatch(host, guest, pids[2], fullTime: true);
            Check(second.Bp == bpLimit && second.Medals > 0,
                "a second Skirmish with a byte-identical result is still settled");
            var shortMatch = PlayMatch(host, guest, pids[2], fullTime: false);
            Check(shortMatch.Bp == 0 && shortMatch.Qt == 0 && shortMatch.Medals == 0,
                "a Skirmish settled a second after it started pays no BP, QT, Battle Medals or Lunarite");
        }
        finally
        {
            _serverEvents.SetPcRoom(pcRoom);
            foreach (int pid in pids)
            {
                _simulationManager.LeaveRoom(pid);
                _playerManager.SetOffline(pid);
            }
            foreach (var link in links)
            {
                link.Client.Dispose();
                link.Peer.Dispose();
            }
        }
    }

    // Rooms take only the room menu's goal scores and time limits, keep their settings while a
    // match runs, and a match is paid by the time limit it started with and the time it ran.
    private void SimulationGoalsAndPlayTimeRewards()
    {
        var links = new List<(Client Client, System.Net.Sockets.TcpClient Peer)>();
        var pids = new List<int>();
        bool pcRoom = _serverEvents.PcRoomActive;
        try
        {
            _serverEvents.SetPcRoom(false);
            for (int i = 0; i < 2; i++)
            {
                int pid = _characterStore.Create(959480020 + i, $"SimGoal{i}", 1, new Custom()).Pid;
                ResetCharacter(pid);
                pids.Add(pid);
            }
            var host = OpenSimulationLink(pids[0], links);
            var guest = OpenSimulationLink(pids[1], links);
            _simulationManager.CreateRoom(host.Client, "Goals", 2, 102, 5, 1, 0, 1, 3, "");
            byte[]? oneMinute = ReceiveSimulationOp(host, 131);
            _simulationManager.CreateRoom(host.Client, "Goals", 2, 102, 5, 8, 7, 1, 3, "");
            byte[]? oddScore = ReceiveSimulationOp(host, 131);
            _simulationManager.CreateRoom(host.Client, "Goals", 2, 102, 5, 8, 0, 1, 3, "");
            byte[]? created = ReceiveSimulationOp(host, 131);
            _simulationManager.ChangeRoomInfo(host.Client, 102, 5, 0, 0, 2, 3);
            byte[]? noTime = ReceiveSimulationOp(host, 135);
            Check(oneMinute is [131, SimulationAnswer.Error, ..] && oddScore is [131, SimulationAnswer.Error, ..]
                && created is [131, 0, ..] && noTime is [135, SimulationAnswer.Error]
                && SimulationManager.IsSimulationGoal(2, 1200, 18) && !SimulationManager.IsSimulationGoal(4, 0, 8),
                "Simulation rooms take only the room menu's goal scores and time limits");

            _simulationManager.JoinRoom(guest.Client, created![2], "");
            var rooms = (System.Collections.IDictionary)typeof(SimulationManager)
                .GetField("_simulationRooms", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_simulationManager)!;
            byte[]? midChange = null;
            // Four minutes of an eight-minute match; the room is set to one minute while it runs,
            // by op135 (refused) and behind its back.
            var half = PlaySimulationMatch(host, guest, pids[1], 4 * 60, () =>
            {
                _simulationManager.ChangeRoomInfo(host.Client, 102, 5, 0, 10, 2, 3);
                midChange = ReceiveSimulationOp(host, 135);
                ((SimulationManager.SCRoomInfo)rooms[created[2]]!).GoalTime = 1;
            });
            Check(midChange is [135, SimulationAnswer.AlreadyStart],
                "a running Simulation match's room settings cannot be changed");
            Check(half.Medals == 0 && half.Bp == 1000 && half.Qt > 0
                && half.Qt <= SimulationManager.SimulationQTLimit(4 * 60, false)
                && half.Qt < SimulationManager.SimulationQTLimit(8 * 60, false) / 2 + 100,
                "a Simulation match pays by the time limit it started with and the time it ran");

            ((SimulationManager.SCRoomInfo)rooms[created[2]]!).GoalTime = 8;
            var halfMinute = PlaySimulationMatch(host, guest, pids[1], 30);
            Check(halfMinute.Bp == 0 && halfMinute.Qt == 0 && halfMinute.Medals == 0
                && SimulationManager.SimulationPlayTimeWeight(59, 480) == 0
                && SimulationManager.SimulationPlayTimeWeight(60, 480) > 0,
                "a Simulation match shorter than a minute pays nothing");
        }
        finally
        {
            _serverEvents.SetPcRoom(pcRoom);
            foreach (int pid in pids)
            {
                _simulationManager.LeaveRoom(pid);
                _playerManager.SetOffline(pid);
            }
            foreach (var link in links)
            {
                link.Client.Dispose();
                link.Peer.Dispose();
            }
        }
    }

    // Battlefield rewards mailed on a full inventory keep BIND_WHEN_PICKUP.
    private void MailedBattlefieldRewardsStayBound()
    {
        var harborZone = _battleZoneCatalog.Get(216)!;
        var player = _characterStore.Create(959480010, "BattleMailBind", 1, new Custom());
        ResetCharacter(player.Pid);
        var inventory = _playerStore.GetInventory(player.Pid, () => []);
        int capacity = _playerStore.MainCapacity(player.Pid);
        for (int cell = 0; cell < capacity; cell++)
            inventory.Add(new PlayerStore.GenericItem
                { InventoryIndex = InventoryType.Main, InventoryCell = (byte)cell, ItemId = 110101, Count = 1, Durability = 100 });
        byte[] wonBlock = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(wonBlock.AsSpan(6), 1);
        _battleServerManager.GrantBattleFieldRewards(new BattleResultRequest(player.Pid, 1, 9,
            new byte[27], new byte[12], wonBlock, new byte[104]), harborZone);
        var medal = _transmitStore.List(player.Pid).SingleOrDefault()?.Attachments
            .Select(x => x.Item).FirstOrDefault(x => x.ItemId == BattleServerManager.MedalOfValorItem);
        Check(medal != null && !_playerStore.CanPlayerTradeItem(medal),
            "a mailed Medal of Valor keeps its bind-on-pickup and cannot be traded");
        _transmitStore.DeleteCharacter(player.Pid);
        ResetCharacter(player.Pid);
    }
}
