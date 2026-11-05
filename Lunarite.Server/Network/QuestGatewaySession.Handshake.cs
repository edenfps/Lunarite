using System;
using System.Net;
using System.Net.Sockets;

using Lunarite.Network.Ue3;

using Microsoft.Extensions.Logging;

namespace Lunarite.Network;

internal sealed partial class QuestGatewaySession
{
    public bool CancelHandshake(int ownerPid)
    {
        lock (_lock)
        {
            if (ownerPid <= 0 || _ownerPid != ownerPid) return false;
            ++_handshakeGeneration;
            foreach (var socket in _pool) { try { socket.Close(); } catch { } }
            _pool.Clear();
            try { _udp?.Close(); } catch { }
            _udp = null;
            _pendingAcks.Clear();
            _connected = _loginSent = _serverReady = false;
            _probePid = 0;
            _accountClient = null;
            _roomReady = null;
            _ownerPid = 0;
            _logger.LogInformation("[Gateway] canceled quest transport for host PID {0}.", ownerPid);
            return true;
        }
    }

    // Called after op42, when the client hosts the quest map as a UHuxleyP2PNetDriver listen
    // server. Joins as the ?MasterServer net client so HuxleyMultiGame.Login spawns
    // HuxleyMasterServerGateway. Uses P2P framing and retries until the listen world is up.
    public void StartHandshake()
    {
        int generation;
        lock (_lock)
        {
            generation = ++_handshakeGeneration;
            _plainTransport = false; _connected = false; _loginSent = false; _serverReady = false; _probePid = 0;
            _packageMap.Reset();
            foreach (var so in _pool) { try { so.Close(); } catch { } }
            _pool.Clear();
        }
        _logger.LogWarning("[Gateway] handshake to {0}: probe for SERVERREADY, then real HELLO pool.", _target);

        // The host inserts a one-time MMOPermission (~20s timeout) at quest entry. A HELLO that
        // arrives during the async level load consumes it on a transient AsyncWorld connection
        // and its CHALLENGE is lost. Phase 1 probes with an unregistered PID ("Not permission!",
        // nothing consumed) until the session text reports SERVERREADY; phase 2 then sends
        // real-PID HELLOs on fresh ports until one gets CHALLENGE.
        System.Threading.Tasks.Task.Run(async () =>
        {
            // Phase 1, capped at about 12s if no session text arrives.
            lock (_lock) _probePid = _roomPid + 7_000_000;   // never registered
            for (int i = 0; i < 8; i++)
            {
                lock (_lock)
                {
                    if (_handshakeGeneration != generation) return;
                    if (_serverReady || _connected) break;
                }
                AddFreshSocket();
                await System.Threading.Tasks.Task.Delay(1500);
            }
            lock (_lock)
            {
                if (_handshakeGeneration != generation) return;
                _probePid = 0;
            }

            // Phase 2.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                lock (_lock)
                {
                    if (_handshakeGeneration != generation || _connected || _loginSent) return;
                }
                AddFreshSocket();
                await System.Threading.Tasks.Task.Delay(1500);
            }
            lock (_lock)
            {
                if (_handshakeGeneration == generation && !_connected)
                    _logger.LogWarning("[Gateway] no CHALLENGE after probe+pool on {0}.", _target);
            }
        });
    }

    // New ephemeral port with one opening HELLO (pkt#0/seq=1). The oldest socket is closed past PoolMax.
    private void AddFreshSocket()
    {
        Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Any, 0));
        Socket? evict = null;
        lock (_lock)
        {
            _pool.Add(sock);
            _udp = sock;
            _outPacketId = 0; _inPacketId = -1; _outReliableCtrl = 0; _pendingAcks.Clear();
            if (_pool.Count > PoolMax) { evict = _pool[0]; _pool.RemoveAt(0); }
        }
        try { evict?.Close(); } catch { }
        BeginReceiveOn(sock);
        _logger.LogInformation("[Gateway] pool socket {0} (pool={1})", sock.LocalEndPoint, _pool.Count);
        try { SendHello(); }
        catch (Exception ex) { _logger.LogWarning("[Gateway] Hello send failed: {0}", ex.Message); }
    }

    // Use the socket that received CHALLENGE for the rest of the session and close the others.
    // Every pool socket sent one HELLO (pkt#0/seq=1), so the out-state is (1,1) on any of them.
    private void FreezeOnto(Socket x)
    {
        lock (_lock)
        {
            _connected = true;
            _udp = x;
            _outPacketId = 1; _outReliableCtrl = 1;
            _pendingAcks.Clear();
            foreach (var so in _pool) if (!ReferenceEquals(so, x)) { try { so.Close(); } catch { } }
            _pool.Clear(); _pool.Add(x);
        }
        _logger.LogInformation("[Gateway] CHALLENGE on {0} — froze handshake onto this port.", x.LocalEndPoint);
    }

    // The standard UE3 fields (P=/REVISION=/MINVER=) are required by the lower net layer; the MMO
    // VER=3/PID=/KEY= fields are found by substring search in NotifyReceivedText. A HELLO with
    // only the MMO fields is dropped before dispatch. VER must be literally 3 for the MMO form.
    private string HelloText() => _plainTransport
        ? $"HELLO P=1 REVISION=0 MINVER=3077 VER={EngineVersion}"
        : $"HELLO P=1 REVISION=0 MINVER=3077 VER={MmoHelloVer} PID={(_probePid != 0 ? _probePid : _roomPid)} KEY={_roomKeyHex}";

    private void SendHello()
    {
        string hello = HelloText();
        _logger.LogWarning("[Gateway] -> {0} (len={1})", hello, hello.Length);
        var msg = new BitWriter();
        msg.WriteString(hello);

        int outId;
        uint seq;
        var packet = NewPacketWithAcks(out outId);
        lock (_lock) { seq = ++_outReliableCtrl; }

        packet.WriteBit(false);                      // bunch, not ack
        packet.WriteBit(true);                       // bControl (bOpen/bClose follow)
        packet.WriteBit(true);                       // bOpen
        packet.WriteBit(false);                      // bClose
        packet.WriteBit(true);                       // bReliable
        packet.WriteInt(0, MaxChannels);             // ChIndex 0 (control)
        packet.WriteInt(seq, MaxChSequence);         // ChSequence (reliable)
        packet.WriteInt(ChTypeControl, ChTypeMax);   // ChType (bReliable||bOpen)
        packet.WriteInt((uint)msg.NumBits, MaxBunchBits);
        packet.WriteBitsFrom(msg);

        SendPacket(packet, $"NMT_Hello (pkt#{outId})");
    }

    // UHuxleyGameEngine challenge-response (@0x10ABF630); the server checks it before WELCOME.
    private int ChallengeResponse(int challenge)
    {
        unchecked { return (challenge << 16) ^ (237 * challenge) ^ (challenge >> 16) ^ (int)0x93FE92CE; }
    }

    private int ParseIntOption(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return 0;
        i += key.Length;
        int j = i;
        while (j < text.Length && (char.IsDigit(text[j]) || (text[j] == '-' && j == i))) j++;
        return int.TryParse(text.AsSpan(i, j - i), out var value) ? value : 0;
    }

    // "KEY=value" up to the next space.
    private string ParseStrOption(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return "";
        i += key.Length;
        int j = text.IndexOf(' ', i);
        if (j < 0) j = text.Length;
        return text[i..j];
    }

    // Client format: "LOGIN RESPONSE=%i URL=%s", preceded by NETSPEED like the real client.
    private void SendLoginText(int challenge)
    {
        lock (_lock) { if (_loginSent) return; _loginSent = true; }

        int response = ChallengeResponse(challenge);
        SendControlText("NETSPEED 10000", "NETSPEED");
        // A headless test server creates its own local gateway, so it gets the plain URL.
        string url = _plainTransport ? "?Name=Master" : _masterUrl;
        SendControlText($"LOGIN RESPONSE={response} URL={url}", "LOGIN");
        _logger.LogWarning("[Gateway] CHALLENGE={0} -> RESPONSE={1}; sent NETSPEED + LOGIN.", challenge, response);
    }

    // One text command as a reliable bunch on the already-open control channel.
    private void SendControlText(string text, string label)
    {
        var msg = new BitWriter();
        msg.WriteString(text);

        int outId; uint seq;
        var packet = NewPacketWithAcks(out outId);
        lock (_lock) { seq = ++_outReliableCtrl; }
        packet.WriteBit(false);                       // bunch (not ack)
        packet.WriteBit(false);                       // bControl = 0 (channel already open)
        packet.WriteBit(true);                        // bReliable
        packet.WriteInt(0, MaxChannels);              // ChIndex 0 (control)
        packet.WriteInt(seq, MaxChSequence);          // ChSequence
        packet.WriteInt(ChTypeControl, ChTypeMax);    // ChType (present when bReliable||bOpen)
        packet.WriteInt((uint)msg.NumBits, MaxBunchBits);
        packet.WriteBitsFrom(msg);

        SendPacket(packet, $"{label} (pkt#{outId}, seq={seq})");
    }
}
