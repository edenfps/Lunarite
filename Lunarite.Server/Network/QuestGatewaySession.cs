using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using Lunarite.Network.Ue3;
using Lunarite.Packets.Outgoing;

using Microsoft.Extensions.Logging;

namespace Lunarite.Network;

// Minimal UE3 (build 3240) net client that joins a client-hosted quest instance as the
// ?MasterServer gateway. Transport, bunch layout, handshake and env-var entry points are
// described in docs/research/protocol-notes.md.
internal sealed partial class QuestGatewaySession
{
    private readonly ILogger _logger;

    public QuestGatewaySession(ILogger<QuestGatewaySession> logger)
    {
        _logger = logger;
    }

    private const int P2PPort = 7777;

    private const uint MaxPacketId = 16384;   // MAX_PACKETID
    private const uint MaxChannels = 1023;    // MAX_CHANNELS
    private const uint MaxChSequence = 1024;  // MAX_CHSEQUENCE
    private const uint MaxBunchBits = 512 * 8; // MaxPacket*8, MaxPacket about 512
    private const uint ChTypeMax = 8;
    private const uint ChTypeControl = 1;     // CHTYPE_Control
    private const int EngineVersion = 3240;
    // FClassNetCache for HuxleyQuestMasterServerGateway (rebuilt from Core/Engine/HuxleyGame .u
    // inheritance and declaration order) has 135 fields: 131=bRemoteGateway,
    // 132=MasterServerEventFired, 133=FinishRallyPoint, 134=ServerSetObjectiveComplete.
    // Field indices use value-aware SerializeInt(Max=135).
    private const uint GatewayNetFieldCount = 135;
    private const uint MasterServerEventFiredField = 132;
    private const uint FinishRallyPointField = 133;
    private readonly Ue3PackageMapCatalog _packageMap = new();

    private const byte NMT_Hello = 0;
    private const byte NMT_Login = 5;

    private Socket? _udp;               // send socket: newest pool socket, then the one that got CHALLENGE
    // The listen host is the client that requested op42; retargeted from its TCP peer address.
    private IPEndPoint _target = new(IPAddress.Loopback, P2PPort);

    // Each fresh socket gets a new MMOPermission check (the host only verifies a connection's
    // first HELLO). Earlier ports stay open because CHALLENGE returns on whichever port matched.
    private readonly List<Socket> _pool = [];
    // Large enough that the first HELLO's port survives the ~10s async world load.
    private const int PoolMax = 16;

    private sealed record RecvState(Socket Sock, byte[] Buf);

    private int _outPacketId;
    private int _inPacketId = -1;
    private uint _outReliableCtrl;     // reliable sequence for control channel (0)
    private readonly List<int> _pendingAcks = [];  // piggybacked on the next outgoing packet
    private bool _connected;
    private bool _loginSent;
    private bool _serverReady;         // instance sent "SERVERREADY" (level loaded)
    private int _handshakeGeneration;  // cancels retry workers from a superseded room
    private int _probePid;             // nonzero: HELLO uses this unregistered PID, which never
                                              // consumes the one-time permission
    private int _ue3RecvCount;
    // Plain-UDP TcpNetDriver target (headless test server): no 0x02 P2P suffix, no pings.
    private bool _plainTransport;
    // ?MasterServer makes HuxleyMultiGame.Login create the remote gateway. At JOIN, native
    // UWorld::NotifyReceivedText checks HasOption("Master") on the hosted world URL (set by
    // QuestCreate.HostMap), not this login URL; that path binds NetDriver+88 without
    // HuxleyQuestGame.Login's invalid gateway-to-player-controller cast.
    private string _masterUrl = "?Name=Master?Master?MasterServer";
    private readonly object _lock = new();

    // MMO HELLO identity (UWorld::NotifyReceivedText @0x10ADCA80). On an NM_ListenServer quest
    // host dword_1239A7A0 = g_lpClientTcpLink, which forces the MMO HELLO path:
    // "HELLO VER=3 PID=<nonzero> KEY=<32 hex>", validated against the MMOPermission table
    // (sub_11197060). KEY is op42.ConnData as %08X%08X%08X%08X (parsed by sub_1096A0D0); PID is
    // the room player id (op42 field #3).
    private const int MmoHelloVer = 3;
    private int _roomPid = 810101;                      // op42 QuestIndex (room PID)
    private int _ownerPid;                              // MMO character hosting this room
    private string _roomKeyHex = new('0', 32);          // op42.ConnData as 32 hex chars

    // Account TCP link (g_lpClientTcpLink) captured at op42; op59 loot replies go here.
    private Client? _accountClient;
    private Action<int, int>? _roomReady;

    // pid and connData must match what was sent in op42 so the HELLO KEY validates.
    public void ConfigureRoom(int pid, byte[] connData, Client? client = null,
        IPAddress? hostAddress = null, int ownerPid = 0, Action<int, int>? roomReady = null,
        ushort hostPort = P2PPort)
    {
        lock (_lock)
        {
            _accountClient = client;
            _roomPid = pid;
            _ownerPid = ownerPid;
            _roomReady = roomReady;
            IPAddress selected = hostAddress ?? client?.IPAddress ?? IPAddress.Loopback;
            string? configured = Environment.GetEnvironmentVariable("QUEST_GATEWAY_IP");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (IPAddress.TryParse(configured, out var parsed)) selected = parsed;
                else _logger.LogWarning("[Gateway] ignoring invalid QUEST_GATEWAY_IP '{0}'.", configured);
            }
            selected = selected.Equals(IPAddress.None) ? IPAddress.Loopback : selected.MapToIPv4();
            _target = new IPEndPoint(selected, hostPort);
            // The host stores ConnData dwords little-endian; the KEY= parser (sub_1096A0D0)
            // reads each 8-hex group as a number, so each LE dword is formatted as %08X.
            if (connData is { Length: 16 })
            {
                var sb = new System.Text.StringBuilder(32);
                for (int i = 0; i < 16; i += 4)
                    sb.Append(BinaryPrimitives.ReadUInt32LittleEndian(connData.AsSpan(i)).ToString("X8"));
                _roomKeyHex = sb.ToString();
            }
            else _roomKeyHex = new string('0', 32);
        }
    }

    // Returns a copy because IPEndPoint is mutable.
    public IPEndPoint TargetEndpoint
    {
        get { lock (_lock) return new IPEndPoint(_target.Address, _target.Port); }
    }

    // op59 OnQuestListenDropItem on the account link; the client spawns HuxleyQuestPickup actors.
    // enemyPid places the pickup; drops are (itemIndex, count) pairs.
    public void GrantLoot(int enemyPid, IReadOnlyList<(int item, int num)> drops)
    {
        var client = _accountClient;
        if (client == null) { _logger.LogWarning("[Gateway] GrantLoot: no account client bound."); return; }
        var packet = new QuestListenDropItem { Opcode = 59, Pid = enemyPid };
        foreach (var (item, num) in drops) packet.Drops.Add(new QuestListenDropItem.Drop(item, num));
        client.SendPacket(packet);
        _logger.LogInformation("[Gateway] GrantLoot -> op59 PID={0} drops={1}", enemyPid,
            string.Join(",", drops.Select(d => $"{d.item}x{d.num}")));
    }

    private bool _captureMode;

    private void EnsureStarted(int bindPort = 0)
    {
        if (_udp != null)
            return;
        _udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _udp.Bind(new IPEndPoint(IPAddress.Any, bindPort));
        _logger.LogInformation("[Gateway] UDP endpoint bound at {0}.", _udp.LocalEndPoint);
        BeginReceive();
    }

    // Binds :7777 and logs what a real client sends (HuxleyMMOGame.exe 127.0.0.1:7777).
    public void StartCapture()
    {
        lock (_lock) _captureMode = true;
        EnsureStarted(P2PPort);
        _logger.LogWarning("[Gateway] CAPTURE mode: listening on :{0} for a real client's handshake.", P2PPort);
    }

    // Relays :7777 to a real dedicated server on :7778 and logs both directions.
    private bool _proxyMode;
    private readonly IPEndPoint _proxyServer = new(IPAddress.Loopback, 7778);
    private EndPoint? _proxyClient;

    public void StartProxy()
    {
        lock (_lock) _proxyMode = true;
        EnsureStarted(P2PPort);
        _logger.LogWarning("[Gateway] PROXY mode: :{0} <-> server :7778.", P2PPort);
    }

    // Pool sockets are armed concurrently, so each receive gets its own buffer.
    private void BeginReceiveOn(Socket socket)
    {
        try
        {
            var state = new RecvState(socket, new byte[2048]);
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            socket.BeginReceiveFrom(state.Buf, 0, state.Buf.Length, SocketFlags.None, ref from, OnReceive, state);
        }
        catch (ObjectDisposedException) { /* socket closed */ }
        catch (Exception ex) { _logger.LogWarning("[Gateway] arm failed: {0}", ex.Message); }
    }

    // Test, capture and proxy modes only use _udp.
    private void BeginReceive() { if (_udp != null) BeginReceiveOn(_udp); }

    private bool IsLive(Socket socket)
    {
        lock (_lock) return ReferenceEquals(socket, _udp) || _pool.Contains(socket);
    }

    // Handshake against a headless dedicated server (plain UDP TcpNetDriver on 127.0.0.1:7777).
    public void StartTest()
    {
        lock (_lock)
        {
            _plainTransport = true; _outPacketId = 0; _inPacketId = -1; _outReliableCtrl = 0;
            _connected = false; _loginSent = false;
        }
        EnsureStarted();
        StartAckFlush();
        _logger.LogWarning("[Gateway] TEST mode: plain-UDP handshake to {0}", _target);
        foreach (var delayMs in new[] { 1000, 3000, 5000, 7000 })
            System.Threading.Tasks.Task.Delay(delayMs).ContinueWith(_ =>
            {
                lock (_lock) { if (_connected) return; }
                try { SendHello(); } catch (Exception ex) { _logger.LogWarning("[Gateway] Hello failed: {0}", ex.Message); }
            });
    }

    // Every actor-channel bunch, appended verbatim.
    private readonly string _actorCapPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "actor_bunches.log");
    // Only the gateway actor (ch=2), including inherited PlayerController RPCs.
    private readonly string _ch2RpcPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "ch2_rpc.log");
    private int _actorBunchCount;
    private int _masterEventCount;

    // Largest bunch per channel. Enemy pawns (HuxleyMMOCharacter) open with 380-940+ bits;
    // projectiles and effects stay under ~260. An enemy's channel closes when it dies.
    private readonly Dictionary<uint, int> _chMaxBits = new();
    // Diagnostic only; completion telemetry is persisted per PID/quest in QuestStore.
    private int _killsThisQuest;
    // A channel close carries no enemy EntryIndex, so loot comes from client op56 instead.
    private readonly int _enemyBitsThreshold =
        int.TryParse(Environment.GetEnvironmentVariable("ENEMY_BITS"), out var t) ? t : 350;

    // Sends ack-only packets every 30ms while acks are pending. One loop per session.
    private bool _ackFlushStarted;

    private IEnumerable<IPEndPoint> Candidates()
    {
        yield return TargetEndpoint;
    }

    public void SendPingProbe(uint tick)
    {
        EnsureStarted();
        var ping = new byte[12];
        ping[0] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(ping.AsSpan(1), 0x4D415354);
        ping[5] = 1; ping[6] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(ping.AsSpan(7), tick);
        ping[11] = 4;
        foreach (var ep in Candidates())
            try { _udp!.SendTo(ping, ep); } catch { }
    }
}
