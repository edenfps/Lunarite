using System.Buffers.Binary;
using System.Net;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void ListenersAndAdvertisedEndpoints()
    {
        // Stopping a listener completes its pending asynchronous accept with an expected disposal error.
        // Exercise the real lifecycle repeatedly so normal shutdown cannot terminate the host process.
        for (int i = 0; i < 16; i++)
        {
            var lifecycleServer = new Server(0, HandlerType.Agent, static (_, _) => { }, _loggerFactory);
            lifecycleServer.Start();
            lifecycleServer.Stop();
        }
        Thread.Sleep(100);
        Check(true, "listeners stop cleanly while asynchronous accepts are pending");

        // A client that hangs up before the listener records it must not stay listed: a stale
        // Agent entry keeps its account logged in. A slow "Accepted" log line (a busy console)
        // gives each reset peer time to be torn down mid-accept.
        var churnServer = new Server(0, HandlerType.Agent, static (_, _) => { },
            new StallingLoggerFactory(_loggerFactory, "Accepted client connection", 50));
        churnServer.Start();
        int churnPort = ((IPEndPoint)churnServer.LocalEndpoint).Port;
        for (int i = 0; i < 16; i++)
        {
            using var churnPeer = new System.Net.Sockets.TcpClient { LingerState = new(true, 0) };
            churnPeer.Connect(IPAddress.Loopback, churnPort);
            // The version packet means the client exists and is reading; hang up mid-accept.
            churnPeer.ReceiveTimeout = 2000;
            churnPeer.GetStream().ReadExactly(new byte[2]);
        }
        var churnDeadline = DateTime.UtcNow.AddSeconds(5);
        while (churnServer.Clients.Length != 0 && DateTime.UtcNow < churnDeadline) Thread.Sleep(20);
        Check(churnServer.Clients.Length == 0, "clients that disconnect while being accepted leave no stale entry");
        churnServer.Stop();

        // Sends neither stall nor drop a client that is busy sending: 200 packets out while the
        // peer streams frames in, and the link stays up.
        var busyListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        busyListener.Start();
        using var busyPeer = new System.Net.Sockets.TcpClient();
        busyPeer.Connect(IPAddress.Loopback, ((IPEndPoint)busyListener.LocalEndpoint).Port);
        using var busyAccepted = busyListener.AcceptTcpClient();
        busyListener.Stop();
        using var busyClient = new Client(920098,
            new Server(0, HandlerType.Agent, static (_, _) => { }, _loggerFactory), busyAccepted);
        var busyStream = busyPeer.GetStream();
        var drain = Task.Run(() =>
        {
            var sink = new byte[65536];
            try { while (busyStream.Read(sink) > 0) { } } catch (IOException) { } catch (ObjectDisposedException) { }
        });
        // Valid encrypted op255 frames, so the inbound side is busy but silent.
        var floodFrames = new byte[2000 * 4];
        for (int i = 0; i < 2000; i++)
        {
            byte[] frame = [4, 0, 0, 0xFF];
            uint sum = (uint)(frame[1] ^ (frame[0] << 8)) + (uint)(frame[3] ^ (frame[2] << 8));
            while (sum >> 16 > 0) sum = (sum & 0xFFFF) + (sum >> 16);
            frame[2] = (byte)~sum;
            for (int b = 2; b < 4; b++)
                for (int x = 0; x < 256; x++)
                    if (busyClient.DecryptTable[i % 64, x] == frame[b]) { frame[b] = (byte)x; break; }
            frame.CopyTo(floodFrames, i * 4);
        }
        var flood = Task.Run(() => busyStream.Write(floodFrames));
        var sendClock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) busyClient.SendPacket(new NewMember { Answer = 0 });
        sendClock.Stop();
        flood.Wait();
        Thread.Sleep(100);
        Check(sendClock.ElapsedMilliseconds < 150 && busyClient.IsConnected,
            "200 sends to a client that is streaming frames in finish quickly and keep it connected");
        Check((int)busyAccepted.Client.GetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.KeepAlive)! != 0
            && (int)busyAccepted.Client.GetSocketOption(System.Net.Sockets.SocketOptionLevel.Tcp,
                System.Net.Sockets.SocketOptionName.TcpKeepAliveTime)! == 30,
            "client links probe a silent peer so a vanished player is disconnected");
        busyPeer.Close();
        drain.Wait();

        var lanGameIp = IPAddress.Parse("192.168.50.12");
        var publicGameIp = IPAddress.Parse("203.0.113.9");
        Check(Agent.ResolveGameAddress(null, IPAddress.Loopback).Equals(IPAddress.Loopback)
            && Agent.ResolveGameAddress(null, lanGameIp).Equals(lanGameIp)
            && Agent.ResolveGameAddress("203.0.113.9", lanGameIp).Equals(publicGameIp)
            && Agent.ResolveGameAddress(null, IPAddress.IPv6Any).Equals(IPAddress.Loopback),
            "Agent advertises its accepted IPv4 interface or an explicit public game endpoint");
        bool badGameAddressRejected = false;
        try { Agent.ResolveGameAddress("not-an-ip", lanGameIp); }
        catch (InvalidOperationException) { badGameAddressRejected = true; }
        bool multicastGameAddressRejected = false;
        try { Agent.ResolveGameAddress("224.0.0.1", lanGameIp); }
        catch (InvalidOperationException) { multicastGameAddressRejected = true; }
        Check(badGameAddressRejected && multicastGameAddressRejected,
            "Agent rejects invalid public game endpoints before clients can select the server");
        bool badNatTestAddressRejected = false;
        try { Agent.ResolveGameAddress("not-an-ip", lanGameIp, "HUXLEY_P2P_TEST_PUBLIC_IP"); }
        catch (InvalidOperationException ex)
        {
            badNatTestAddressRejected = ex.Message.Contains("HUXLEY_P2P_TEST_PUBLIC_IP");
        }
        Check(badNatTestAddressRejected
            && Agent.ResolveGameAddress("203.0.113.9", IPAddress.Loopback,
                "HUXLEY_P2P_TEST_PUBLIC_IP").Equals(publicGameIp),
            "NAT test override validates IPv4 and explicitly applies to loopback clients");
        var advertisedServerList = new ServerList
        {
            Servers = { new ServerList.GameServer
                { Server = 1, IP = lanGameIp, Name = "Test", Percet = 0, Camp = 10 } }
        }.Serialize();
        var advertisedSelection = new Lunarite.Packets.Outgoing.SelectServer
            { Answer = 0, IP = lanGameIp, Port = 45406 }.Serialize();
        Check(advertisedServerList.AsSpan(4, 4).SequenceEqual(lanGameIp.GetAddressBytes())
            && advertisedSelection.SequenceEqual(new byte[] { 9, 0, 192, 168, 50, 12, 0x5E, 0xB1 }),
            "server-list and selection packets serialize the same reachable LAN IPv4 address");

        // Dependency-free integration checks for persistent mail primitives. AppContext.BaseDirectory is
        // this test executable's bin directory, so its huxley.db cannot touch the running server's data.
        _sqliteStore.Close();
        foreach (string suffix in new[] { "", "-wal", "-shm" }) File.Delete(_sqliteStore.DatabasePath + suffix);
        foreach (string name in new[] { "accounts.json", "characters.json", "character-sequence.json", "playerdata.json", "playerprogress.json", "playerinventory.json", "transmits.json", "playerquests.json", "battleresults.json", "personalbuffs.json", "durativeitems.json", "playerlicenses.json", "playerrecipes.json", "friends.json", "blockedplayers.json", "clans.json", "auctions.json", "auction-sequence.json", "payitems.json" })
            foreach (string suffix in new[] { "", ".tmp", ".bak", ".imported", ".bak.imported" })
                File.Delete(Path.Combine(AppContext.BaseDirectory, name + suffix));
    }

    private void NatPackets()
    {
        Check(new NATTypeResult { Result = 1 }.Serialize().SequenceEqual(new byte[] { 123, 1 })
            && new HoleResult { Result = 0, Playable = 1 }.Serialize().SequenceEqual(new byte[] { 125, 0, 1 }),
            "server op123/op125 serialize the native NAT and hole-result byte fields");
        var holeEndpoints = new HoleStart
        {
            PublicEndpoint = new P2PEndpoint(unchecked((int)0x01020304), 7777),
            PeerEndpoint = new P2PEndpoint(unchecked((int)0xAABBCCDD), 8888)
        }.Serialize();
        Check(holeEndpoints.SequenceEqual(new byte[] { 124, 4, 3, 2, 1, 0x61, 0x1E, 0xDD, 0xCC, 0xBB, 0xAA, 0xB8, 0x22 }),
            "server op124 serializes exact dwdw public/peer endpoint fields");
        var natTest = new NATTest();
        natTest.Endpoints.Add(new P2PEndpoint(unchecked((int)0x01020304), 7777));
        Check(natTest.Serialize().SequenceEqual(new byte[] { 127, 1, 4, 3, 2, 1, 0x61, 0x1E })
            && new PeerAddrList
            {
                PublicEndpoint = new P2PEndpoint(unchecked((int)0x01020304), 7777),
                PeerEndpoint = new P2PEndpoint(unchecked((int)0xAABBCCDD), 8888)
            }.Serialize().SequenceEqual(new byte[] { 128, 4, 3, 2, 1, 0x61, 0x1E, 0xDD, 0xCC, 0xBB, 0xAA, 0xB8, 0x22 }),
            "server op127/op128 serialize native NAT-test lists and peer endpoint pairs");
        byte[] serverPing = [0, 1, 0, 0, 0, 1, 3, 0x78, 0x56, 0x34, 0x12, 4];
        Check(ServerPingService.BuildReply(serverPing) is { } pingReply && pingReply[0] == 1
            && pingReply.AsSpan(1).SequenceEqual(serverPing.AsSpan(1))
            && ServerPingService.BuildReply(pingReply) == null
            && ServerPingService.BuildReply(serverPing.AsSpan(0, 11)) == null,
            "server-list ping echoes the 12-byte P2P ping with Ack=1 and ignores replies and other frames");
        using (var pingService = new ServerPingService(_loggerFactory.CreateLogger("ServerPing"), 0))
        {
            pingService.Start();
            using var pinger = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            pinger.Send(serverPing, serverPing.Length, new IPEndPoint(IPAddress.Loopback, pingService.Port));
            using var pingTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var pong = pinger.ReceiveAsync(pingTimeout.Token).AsTask().GetAwaiter().GetResult();
            Check(pong.Buffer.SequenceEqual(ServerPingService.BuildReply(serverPing)!),
                "server-list ping service answers a live UDP ping");
        }
        Check(!Agent.IsFull(149, 150, gm: false) && Agent.IsFull(150, 150, gm: false)
            && Agent.IsFull(151, 150, gm: false) && !Agent.IsFull(150, 150, gm: true),
            "a full server refuses server select at capacity, except for GM accounts");
        Check(Agent.LoadPercent(0, 200) == 0 && Agent.LoadPercent(80, 200) == 40
            && Agent.LoadPercent(500, 200) == 100,
            "server-list load percent is online players over capacity, capped at 100");
        using (var natTestService = new P2PNatTestService(_loggerFactory.CreateLogger<P2PNatTestService>()))
        {
            natTestService.Start();
            var announcement = natTestService.BuildAnnouncement(IPAddress.Loopback);
            var advertised = announcement.Serialize();
            var ports = natTestService.Ports;
            Check(ports[0] != ports[1] && advertised.Length == 14 && advertised[0] == 127
                && advertised[1] == 2 && advertised[2] == 127 && advertised[8] == 127,
                "native NAT test advertises two distinct live UDP endpoints");
            using var probeSocket = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            ushort observedPort = checked((ushort)((IPEndPoint)probeSocket.Client.LocalEndPoint!).Port);
            foreach (ushort port in ports)
            {
                probeSocket.Send([4, 0, 0, 0x99], 4, new IPEndPoint(IPAddress.Loopback, port));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var received = probeSocket.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                Check(received.RemoteEndPoint.Port == port
                    && received.Buffer.SequenceEqual(P2PNatTestService.BuildReply(
                        new IPEndPoint(IPAddress.Loopback, observedPort))),
                    "native four-byte NAT probe receives its observed address and port from each test endpoint");
            }
            var natTcpListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            natTcpListener.Start();
            using var natTcpPeer = new System.Net.Sockets.TcpClient();
            natTcpPeer.Connect(IPAddress.Loopback, ((IPEndPoint)natTcpListener.LocalEndpoint).Port);
            using var natTcpAccepted = natTcpListener.AcceptTcpClient();
            natTcpListener.Stop();
            using var natGameClient = new Client(920099,
                new Server(0, HandlerType.Game, static (_, _) => { }, _loggerFactory), natTcpAccepted);
            natTcpPeer.ReceiveTimeout = 3000;
            var natStream = natTcpPeer.GetStream();
            var natDecrypt = natGameClient.DecryptTable;
            byte[] ReadNatGamePacket()
            {
                Span<byte> header = stackalloc byte[2];
                natStream.ReadExactly(header);
                int length = BinaryPrimitives.ReadUInt16LittleEndian(header) - 2;
                byte[] payload = new byte[length];
                natStream.ReadExactly(payload);
                for (int i = 0; i < length; i++) payload[i] = natDecrypt[0, payload[i]];
                return payload;
            }
            _ = ReadNatGamePacket(); // constructor GameVersion
            _accountSessionStore.Issue(IPAddress.Loopback, 920099);
            _p2pManager.NatTestService = natTestService;
            try
            {
                byte[] connect = [0, 127, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                Game.OnHandlePacket(natGameClient, connect);
                var loginPackets = Enumerable.Range(0, 4).Select(_ => ReadNatGamePacket()).ToArray();
                Check(loginPackets[0][0] == 2 && loginPackets[1][0] == 207
                    && loginPackets[2][0] == 10
                    && loginPackets[3].SequenceEqual(advertised),
                    "authenticated Game connection advertises two live NAT endpoints after the roster");
            }
            finally { _p2pManager.NatTestService = null; }
        }
        Check(QuestInstanceManager.BuildQuestHostPeerEndpoints(0x0200007F).Serialize().SequenceEqual(new byte[] {
                128,
                127, 0, 0, 2, 0x61, 0x1E,
                127, 0, 0, 2, 0x61, 0x1E
            }),
            "squad quest guests install the host's canonical UDP peer before op52 travel");
        Check(QuestInstanceManager.BuildQuestHostPeerEndpoints(0x0200007F, 0x0300007F, 7778, 7779).Serialize()
            .SequenceEqual(new byte[] { 128,
                127, 0, 0, 2, 0x62, 0x1E,
                127, 0, 0, 3, 0x63, 0x1E }),
            "guest peer registration preserves distinct NAT-reported public and local UDP ports");
        Check(QuestInstanceManager.QuestHostPeerIp(IPAddress.Parse("::ffff:127.0.0.3"), 0x0200007F) == 0x0300007F
            && QuestInstanceManager.QuestHostPeerIp(IPAddress.Any, 0x0200007F) == 0x0200007F
            && QuestInstanceManager.QuestHostPeerIp(IPAddress.IPv6Any, 0x0200007F) == 0x0200007F
            && QuestInstanceManager.QuestHostPeerIp(IPAddress.Loopback, 0x097100CB) == 0x097100CB
            && QuestInstanceManager.BuildQuestHostPeerEndpoints(0x0200007F, 0x0300007F).Serialize()
                .SequenceEqual(new byte[] { 128,
                    127, 0, 0, 2, 0x61, 0x1E,
                    127, 0, 0, 3, 0x61, 0x1E }),
            "squad quest peer registration preserves distinct public and client-reported local endpoints");
        _guestTravelKey = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();
        _natReportWire = new PacketWriter();
        _natReportWire.Write((byte)155);
        _natReportWire.Write((byte)2);
        _natReportWire.Write(0x0200007F);
        _natReportWire.Write((ushort)7778);
        _natReportWire.Write(0x0300007F);
        _natReportWire.Write((ushort)7779);
        var natReportReader = new PacketReader(_natReportWire.Buffer.AsSpan(1));
        var truncatedNatReportReader = new PacketReader(_natReportWire.Buffer.AsSpan(1, 12));
        Check(P2PNatEndpointReport.TryDeserialize(ref natReportReader, out var parsedNatReport)
            && parsedNatReport is { NatType: 2, PublicAddress: 0x0200007F, PublicPort: 7778,
                LocalAddress: 0x0300007F, LocalPort: 7779 }
            && !P2PNatEndpointReport.TryDeserialize(ref truncatedNatReportReader, out _),
            "native op155 bdwdw report preserves both UDP endpoints and rejects truncation");
        Check(QuestInstanceManager.QuestHostPublicIp(null, 0x0200007F, 0x0100007F) == 0x0200007F
            && QuestInstanceManager.QuestHostPublicIp(null, 0, 0x0100007F) == 0x0100007F
            && QuestInstanceManager.QuestHostPublicIp("127.0.0.4", 0x0200007F, 0x0100007F) == 0x0400007F
            && P2PManager.ReportedNatLocalAddress(0x0300007F)?.ToString() == "127.0.0.3"
            && P2PManager.ReportedNatLocalAddress(0) == null
            && QuestInstanceManager.QuestHostPeerIp(P2PManager.ReportedNatLocalAddress(0x0300007F), 0x0200007F)
                == 0x0300007F,
            "quest host uses authenticated NAT-reported IPs unless an explicit public override is configured");
        var guestTravel = QuestInstanceManager.BuildQuestGuestTravel(0x0200007F, 0x0300007F, _guestTravelKey)
            .Select(packet => packet.Serialize()).ToArray();
        Check(guestTravel.Length == 2 && guestTravel[0][0] == 128 && guestTravel[1][0] == 52
            && guestTravel[1].AsSpan(6, 16).SequenceEqual(_guestTravelKey),
            "squad quest dispatch orders peer registration before keyed op52 travel");
        _guestTravelSeparateJoin = QuestInstanceManager.BuildQuestGuestTravel(0x0100007F, 0x0300007F,
            0x0200007F, _guestTravelKey, 7778, 7779)
            .Select(packet => packet.Serialize()).ToArray();
        Check(_guestTravelSeparateJoin[0].SequenceEqual(QuestInstanceManager.BuildQuestHostPeerEndpoints(
                0x0100007F, 0x0300007F, 7778, 7779).Serialize())
            && _guestTravelSeparateJoin[1].SequenceEqual(new QuestJoin
                { Answer = 0, ServerIP = 0x0200007F, Data = _guestTravelKey }.Serialize()),
            "guest UDP peer endpoints stay separate from the host's UE3 join address");
    }
}

// Delegates to the real loggers, but holds the calling thread on messages containing a marker,
// to widen a race window that a busy console opens in production.
internal sealed class StallingLoggerFactory(ILoggerFactory inner, string marker, int stallMilliseconds) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new StallingLogger(inner.CreateLogger(categoryName), marker, stallMilliseconds);

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() { }

    private sealed class StallingLogger(ILogger inner, string marker, int stallMilliseconds) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains(marker)) Thread.Sleep(stallMilliseconds);
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
