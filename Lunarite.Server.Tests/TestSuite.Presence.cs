using System.Buffers.Binary;
using System.Net;
using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;

internal sealed partial class TestSuite
{
    // A player who closes the game or goes back to character select leaves the screens of everyone
    // in the same city: disconnect clears client.Player before the release broadcasts DelPlayer.
    private void CityLeaveReachesOthers()
    {
        var stayer = _characterStore.Create(939847001, "PresenceStay", 1, new Custom());
        var leaver = _characterStore.Create(939847002, "PresenceLeave", 1, new Custom());
        var stayLink = OpenPresenceSocket(939847001, "127.0.0.4");
        var leaveLink = OpenPresenceSocket(939847002, "127.0.0.5");
        try
        {
            _ = ReceivePresence(stayLink); // GameVersion
            _ = ReceivePresence(leaveLink);
            _playerManager.SetOnline(stayer.Pid, stayLink.Client);
            _playerManager.SetOnline(leaver.Pid, leaveLink.Client);
            stayLink.Client.Player = _playerManager.Get(stayer.Pid);
            leaveLink.Client.Player = _playerManager.Get(leaver.Pid);
            _playerManager.Get(stayer.Pid).CityMap = 18;
            _playerManager.Get(leaver.Pid).CityMap = 18;
            _playerManager.Get(leaver.Pid).TryMarkSpawned();

            DisconnectHandler.HandleDisconnect(leaveLink.Client);
            byte[] seen = ReceivePresence(stayLink);
            Check(seen.SequenceEqual(new DelPlayer { Pid = leaver.Pid }.Serialize()),
                "a player who disconnects is removed from the screens of others in the same city");

            // City travel: the new link selects and spawns the character before the old link closes.
            var oldLink = OpenPresenceSocket(939847003, "127.0.0.6");
            var newLink = OpenPresenceSocket(939847004, "127.0.0.7");
            try
            {
                _ = ReceivePresence(oldLink);
                _ = ReceivePresence(newLink);
                oldLink.Client.Player = _playerManager.Get(leaver.Pid);
                newLink.Client.Player = _playerManager.Get(leaver.Pid);
                _playerManager.SetOnline(leaver.Pid, newLink.Client);
                _playerManager.Get(leaver.Pid).CityMap = 18;
                _playerManager.Get(leaver.Pid).TryMarkSpawned();
                DisconnectHandler.HandleDisconnect(oldLink.Client);
                stayLink.Peer.ReceiveTimeout = 500;
                bool quiet;
                try { _ = ReceivePresence(stayLink); quiet = false; }
                catch (System.IO.IOException) { quiet = true; }
                Check(quiet && _playerManager.Get(leaver.Pid).IsSpawned && _playerManager.Get(leaver.Pid).CityMap == 18
                    && _playerManager.TryGetClient(leaver.Pid, out var live) && ReferenceEquals(live, newLink.Client),
                    "an old Game link closing after city travel leaves the live character in the world");
            }
            finally
            {
                newLink.Client.Player = null;
                oldLink.Client.Player = null;
                oldLink.Client.Dispose(); newLink.Client.Dispose();
                oldLink.Peer.Dispose(); newLink.Peer.Dispose();
            }
        }
        catch (System.IO.IOException)
        {
            Check(false, "a player who disconnects is removed from the screens of others in the same city");
        }
        finally
        {
            DisconnectHandler.HandleDisconnect(stayLink.Client);
            _playerManager.SetOffline(stayer.Pid);
            _playerManager.SetOffline(leaver.Pid);
            stayLink.Client.Player = null;
            leaveLink.Client.Player = null;
            stayLink.Client.Dispose();
            leaveLink.Client.Dispose();
            stayLink.Peer.Dispose();
            leaveLink.Peer.Dispose();
            _characterStore.Delete(stayer.Pid);
            _characterStore.Delete(leaver.Pid);
        }
    }

    private (Client Client, System.Net.Sockets.TcpClient Peer, byte[,] Decrypt) OpenPresenceSocket(int id,
        string localAddress)
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var peer = new System.Net.Sockets.TcpClient(new IPEndPoint(IPAddress.Parse(localAddress), 0));
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var accepted = listener.AcceptTcpClient();
        listener.Stop();
        var gameClient = new Client(id, new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), accepted);
        peer.ReceiveTimeout = 3000;
        return (gameClient, peer, gameClient.DecryptTable);
    }

    private static byte[] ReceivePresence((Client Client, System.Net.Sockets.TcpClient Peer, byte[,] Decrypt) link)
    {
        var stream = link.Peer.GetStream();
        Span<byte> header = stackalloc byte[2];
        stream.ReadExactly(header);
        var payload = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header) - 2];
        stream.ReadExactly(payload);
        for (int i = 0; i < payload.Length; i++) payload[i] = link.Decrypt[0, payload[i]];
        return payload;
    }
}
