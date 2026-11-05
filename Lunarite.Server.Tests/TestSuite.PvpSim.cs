using System.Buffers.Binary;
using System.Net;
using System.Reflection;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Storage;
using Lunarite.World;

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
            _simulationManager.CreateRoom(solo.Client, "Solo", 2, 102, 3, 8, 0, 1, 3, "");
            byte[]? otherType = ReceiveOp(solo, 131);
            _simulationManager.CreateRoom(solo.Client, "Solo", 2, 102, 5, 8, 0, 1, 3, "");
            byte[]? created = ReceiveOp(solo, 131);
            _simulationManager.ChangeRoomInfo(solo.Client, 102, 3, 0, 8, 2, 3);
            byte[]? changed = ReceiveOp(solo, 135);
            _simulationManager.Ready(solo.Client);
            byte[]? soloStart = ReceiveOp(solo, 143);
            Check(otherType is [131, SimulationAnswer.Error, ..] && created is [131, 0, ..]
                && changed is [135, SimulationAnswer.Error]
                && soloStart is [143, SimulationAnswer.PlayerProportion]
                && !SimulationManager.CheckStartCondition(3, 2, new byte[] { 1, 3 }),
                "Simulation rooms take only the room menu's game types and a lone owner cannot start a match");
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
