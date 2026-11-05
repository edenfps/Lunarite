using System.Reflection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    // HuxleyBattleGame.GetPlayerPlayTime: a member who logged in (op109) within the first minute
    // is paid the full BP ceiling however early a score goal ends the match; a later joiner is
    // weighted by the share of the time limit left when they joined, at least 0.1; and a match
    // under a minute still pays nothing.
    private void SimulationBPFollowsRetailPlayTime()
    {
        var links = new List<(Client Client, System.Net.Sockets.TcpClient Peer)>();
        var pids = new List<int>();
        bool pcRoom = _serverEvents.PcRoomActive;
        try
        {
            _serverEvents.SetPcRoom(false);
            for (int i = 0; i < 2; i++)
            {
                int pid = _characterStore.Create(959480040 + i, $"SimJoin{i}", 1, new Custom()).Pid;
                ResetCharacter(pid);
                pids.Add(pid);
            }
            var host = OpenSimulationLink(pids[0], links);
            var guest = OpenSimulationLink(pids[1], links);
            // An eight-minute room with score goal 15.
            _simulationManager.CreateRoom(host.Client, "Joins", 2, 102, 5, 8, 15, 1, 3, "");
            byte[]? created = ReceiveSimulationOp(host, 131);
            _simulationManager.JoinRoom(guest.Client, created![2], "");
            var transports = (System.Collections.IDictionary)typeof(SimulationManager)
                .GetField("_simulationTransports", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_simulationManager)!;
            // The host reports the guest's login `joinedAt` seconds into a match that lasts `seconds`.
            long PlayJoinedAt(int seconds, int joinedAt) => PlaySimulationMatch(host, guest, pids[1], seconds, () =>
            {
                var transport = (SimulationManager.SimulationTransport)transports[pids[0]]!;
                transport.StartedAtUtc = DateTime.UtcNow.AddSeconds(-joinedAt);
                _simulationManager.SimulationJoinedPlayerState(host.Client, pids[1], 2);
                transport.StartedAtUtc = DateTime.UtcNow.AddSeconds(-seconds);
            }).Bp;

            long earlyGoal = PlayJoinedAt(4 * 60, 30);
            Check(earlyGoal == 1000,
                "a member present from the start of a score-goal match that ends early is paid the full BP ceiling");
            long lateJoiner = PlayJoinedAt(8 * 60, 4 * 60);
            long lastMinute = PlayJoinedAt(8 * 60, 7 * 60 + 30);
            Check(lateJoiner == 500 && lastMinute == 100,
                "a Simulation member who joined late is paid BP for the share of the time limit left");
            long tooShort = PlayJoinedAt(59, 0);
            Check(tooShort == 0 && SimulationManager.SimulationPlayTimeWeight(60, 480, 60) == 1.0
                && SimulationManager.SimulationPlayTimeWeight(480, 480, 61) < 1.0
                && SimulationManager.SimulationPlayTimeWeight(480, 480, 420) == 0.1,
                "Simulation BP weights follow retail GetPlayerPlayTime above the one-minute minimum");
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
}
