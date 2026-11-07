using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Lunarite.Network;

// The client's native relay (FP2PSocket), used when a mission guest cannot reach its host directly.
// Guest to relay: type-187 flag 0, addressed by host PID. Host to relay: type-187 flag 1, addressed
// by the guest endpoint it learned. Relay to either: payload, ip4, port (network order), 01.
// The host needs no setup: its 5-second keepalive already holds a NAT mapping to this socket.
public sealed class P2PRelayTable
{
    private const byte RelayFrame = 187;
    private const byte Relayed = 1;
    private static readonly TimeSpan SightingLife = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SessionLife = TimeSpan.FromHours(2);

    private sealed class Session
    {
        public required IPAddress HostIp;
        public required ushort HostPort;
        public required byte[] Trailer;
        public readonly HashSet<IPAddress> GuestIps = [];
        public readonly HashSet<IPEndPoint> Guests = [];
        public DateTime LastUsed = DateTime.UtcNow;
    }

    private readonly object _lock = new();
    private readonly Dictionary<int, Session> _sessions = [];
    private readonly Dictionary<(int Socket, IPEndPoint Source), DateTime> _seen = [];

    // trailer is the endpoint the guest was given in op124: a relayed frame from anywhere else is
    // dropped by the guest.
    public void Open(int hostPid, IPAddress hostIp, ushort hostPort, IPAddress trailerIp, ushort trailerPort,
        IPAddress guestIp)
    {
        byte[] trailer = new byte[7];
        trailerIp.GetAddressBytes().CopyTo(trailer, 0);
        BinaryPrimitives.WriteUInt16BigEndian(trailer.AsSpan(4), trailerPort);
        trailer[6] = Relayed;
        lock (_lock)
        {
            foreach (int stale in _sessions.Where(x => DateTime.UtcNow - x.Value.LastUsed > SessionLife)
                         .Select(x => x.Key).ToList())
                _sessions.Remove(stale);
            if (!_sessions.TryGetValue(hostPid, out var session) || !session.HostIp.Equals(hostIp))
                _sessions[hostPid] = session = new Session { HostIp = hostIp, HostPort = hostPort, Trailer = trailer };
            session.HostPort = hostPort;
            session.Trailer = trailer;
            session.GuestIps.Add(guestIp);
            session.LastUsed = DateTime.UtcNow;
        }
    }

    public void Close(int hostPid, IPAddress guestIp)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(hostPid, out var session)) return;
            session.GuestIps.Remove(guestIp);
            session.Guests.RemoveWhere(x => x.Address.Equals(guestIp));
            if (session.GuestIps.Count == 0) _sessions.Remove(hostPid);
        }
    }

    // A NAT probe or keepalive shows which public port a client's game socket has on this relay
    // socket. With a symmetric NAT it differs per socket.
    public void NoteClient(int socket, IPEndPoint source)
    {
        lock (_lock)
        {
            if (_seen.Count >= 4096)
                foreach (var old in _seen.Where(x => DateTime.UtcNow - x.Value > SightingLife).Select(x => x.Key).ToList())
                    _seen.Remove(old);
            _seen[(socket, source)] = DateTime.UtcNow;
        }
    }

    // The datagram to send on, and where, or null to drop it.
    public (byte[] Data, IPEndPoint To)? Route(int socket, ReadOnlySpan<byte> frame, IPEndPoint source)
    {
        if (frame.Length < 13 || frame[2] != 0 || frame[3] != RelayFrame
            || BinaryPrimitives.ReadUInt16LittleEndian(frame) != frame.Length) return null;
        lock (_lock)
        {
            if (frame[8] == 0) return FromGuest(socket, frame, source);
            return frame[8] == 1 ? FromHost(frame, source) : null;
        }
    }

    private (byte[] Data, IPEndPoint To)? FromGuest(int socket, ReadOnlySpan<byte> frame, IPEndPoint source)
    {
        int hostPid = BinaryPrimitives.ReadInt32LittleEndian(frame[9..]);
        var payload = frame[13..];
        if (payload.IsEmpty || !_sessions.TryGetValue(hostPid, out var session)
            || !session.GuestIps.Any(ip => SameMachine(ip, source.Address))
            || FindHost(socket, session) is not { } host) return null;
        session.Guests.Add(source);
        session.LastUsed = DateTime.UtcNow;
        byte[] trailer = new byte[7];
        source.Address.GetAddressBytes().CopyTo(trailer, 0);
        BinaryPrimitives.WriteUInt16BigEndian(trailer.AsSpan(4), checked((ushort)source.Port));
        trailer[6] = Relayed;
        return (Join(payload, trailer), host);
    }

    private (byte[] Data, IPEndPoint To)? FromHost(ReadOnlySpan<byte> frame, IPEndPoint source)
    {
        if (frame.Length <= 15) return null;
        var guest = new IPEndPoint(new IPAddress(frame.Slice(9, 4)), BinaryPrimitives.ReadUInt16BigEndian(frame[13..]));
        foreach (var session in _sessions.Values)
        {
            if (!SameMachine(session.HostIp, source.Address) || !session.Guests.Contains(guest)) continue;
            session.LastUsed = DateTime.UtcNow;
            return (Join(frame[15..], session.Trailer), guest);
        }
        return null;
    }

    private IPEndPoint? FindHost(int socket, Session session)
    {
        var sightings = _seen.Where(x => x.Key.Socket == socket && SameMachine(x.Key.Source.Address, session.HostIp)
                && DateTime.UtcNow - x.Value <= SightingLife).ToList();
        if (sightings.Count == 0) return null;
        var reported = sightings.FirstOrDefault(x => x.Key.Source.Port == session.HostPort);
        return reported.Key.Source ?? sightings.MaxBy(x => x.Value).Key.Source;
    }

    // Clients on the server's PC use -MULTIHOME 127.0.0.x for TCP, while UDP arrives from 127.0.0.1.
    private static bool SameMachine(IPAddress a, IPAddress b) =>
        a.Equals(b) || (IPAddress.IsLoopback(a) && IPAddress.IsLoopback(b));

    private static byte[] Join(ReadOnlySpan<byte> payload, byte[] trailer)
    {
        byte[] data = new byte[payload.Length + trailer.Length];
        payload.CopyTo(data);
        trailer.CopyTo(data, payload.Length);
        return data;
    }
}
