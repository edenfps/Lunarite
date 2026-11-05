using System.Buffers.Binary;
using System.Net;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    // A Skirmish room driven through SimulationManager with encrypted Game sockets: squads on
    // join, the owner's early Start, then the listen host's op115/op114 result settlement.
    private void SkirmishRoomsAndResults()
    {
        var links = new List<(Client Client, System.Net.Sockets.TcpClient Peer, byte[,] Decrypt)>();
        var pids = new List<int>();
        (Client Client, System.Net.Sockets.TcpClient Peer, byte[,] Decrypt) OpenLink(int pid)
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
            var link = (gameClient, peer, gameClient.DecryptTable);
            links.Add(link);
            return link;
        }
        // The next packet with this opcode, or null when none arrives within the timeout.
        byte[]? ReceiveOp((Client Client, System.Net.Sockets.TcpClient Peer, byte[,] Decrypt) link, byte opcode)
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
                    for (int i = 0; i < payload.Length; i++) payload[i] = link.Decrypt[0, payload[i]];
                    if (payload.Length != 0 && payload[0] == opcode) return payload;
                }
            }
            catch (IOException) { return null; }
        }
        byte[] Result(int pid, int bp)
        {
            var wire = new byte[4 + 1 + 1 + 27 + 12 + 48 + 104];
            BinaryPrimitives.WriteInt32LittleEndian(wire, pid);
            wire[4] = 1; wire[5] = 9;
            int battle = 6 + 27 + 12;
            BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(battle), pid);
            wire[battle + 5] = 5;
            BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(battle + 30), bp);
            return wire;
        }
        void Report((Client Client, System.Net.Sockets.TcpClient Peer, byte[,] Decrypt) reporter, int pid, int bp)
        {
            var reader = new PacketReader(Result(pid, bp));
            BattleResultHandler.HandlePacket(reporter.Client, ref reader);
        }
        int Medals(int pid) => _playerStore.GetInventory(pid, () => [])
            .Where(x => x.ItemId == BattleManager.BattleMedalItem).Sum(x => x.Count);
        try
        {
            for (int i = 0; i < 6; i++)
            {
                int pid = _characterStore.Create(959470000 + i, $"Skirmish{i}", 1, new Custom()).Pid;
                ResetCharacter(pid);
                pids.Add(pid);
            }
            var owner = OpenLink(pids[0]);
            var guests = pids.Skip(1).Take(4).Select(OpenLink).ToArray();
            int outsider = pids[5];

            _simulationManager.CreateRoom(owner.Client, "Skirmish", 16, 102, 5, 10, 0, 1, 3, "");
            byte roomId = ReceiveOp(owner, 131)![2];
            foreach (var guest in guests) _simulationManager.JoinRoom(guest.Client, roomId, "");
            var ownerSquad = new HashSet<int>();
            _simulationManager.AddTeamChatRecipients(pids[0], ownerSquad);
            var fifthSquad = new HashSet<int>();
            _simulationManager.AddTeamChatRecipients(pids[4], fifthSquad);
            Check(ownerSquad.SetEquals(pids.Take(4)) && fifthSquad.SetEquals([pids[4]]),
                "Skirmish joiners fill the first squad on their side with room (4 per squad), not always squad 0");

            _simulationManager.Ready(owner.Client);
            byte[]? earlyStart = ReceiveOp(owner, 143);
            foreach (var guest in guests) _simulationManager.Ready(guest.Client);
            byte[]? afterGuestsReady = ReceiveOp(owner, 143);
            _simulationManager.Ready(owner.Client);
            byte[]? start = ReceiveOp(owner, 143);
            Check(earlyStart is [143, SimulationAnswer.NoReady] && afterGuestsReady == null
                && start is [143, 0],
                "owner Start before every guest is ready answers op143 NO_READY and leaves the owner able to start again");

            long bpBefore = _playerStore.GetProgress(pids[1]).BP;
            Report(owner, pids[1], 700);
            bool runningRejected = _playerStore.GetProgress(pids[1]).BP == bpBefore;
            _simulationManager.StartMatch(owner.Client);
            // The 10-minute match ran its course; a match under a minute pays nothing.
            if (((System.Collections.IDictionary)typeof(SimulationManager)
                    .GetField("_simulationTransports", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(_simulationManager)!)[pids[0]] is SimulationManager.SimulationTransport transport)
                transport.StartedAtUtc = DateTime.UtcNow.AddMinutes(-10);
            _simulationManager.BattleResultSecret(owner.Client);
            Report(guests[0], pids[1], 700);
            bool guestRejected = _playerStore.GetProgress(pids[1]).BP == bpBefore;
            Report(owner, pids[1], 700);
            bool hostCredited = _playerStore.GetProgress(pids[1]).BP == bpBefore + 700;
            Report(owner, pids[1], 900);
            bool replayRejected = _playerStore.GetProgress(pids[1]).BP == bpBefore + 700;
            long outsiderBefore = _playerStore.GetProgress(outsider).BP;
            Report(owner, outsider, 700);
            bool outsiderRejected = _playerStore.GetProgress(outsider).BP == outsiderBefore && Medals(outsider) == 0;
            Check(runningRejected && guestRejected && hostCredited && replayRejected && outsiderRejected,
                "op114 settles only from the host of a just-finished Skirmish, once per member of that match");
            Check(_battleResultStore.ForPid(pids[1]).Last() is { Source: BattleResultStore.SourceLocal, Seconds: >= 1 },
                "Skirmish results keep the match play time measured before op115 ended the transport");

            foreach (var guest in guests) _simulationManager.Ready(guest.Client);
            _simulationManager.Ready(owner.Client);
            byte[]? restart = ReceiveOp(owner, 143);
            _simulationManager.BattleResultSecret(owner.Client);
            long instantBefore = _playerStore.GetProgress(pids[2]).BP;
            Report(owner, pids[2], 700);
            Check(restart is [143, 0] && _playerStore.GetProgress(pids[2]).BP == instantBefore,
                "a Skirmish ended before MatchInProgress (op141) settles no op114 rewards");
        }
        finally
        {
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
}
