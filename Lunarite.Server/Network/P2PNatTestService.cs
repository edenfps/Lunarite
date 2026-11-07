using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Lunarite.Packets.Outgoing;
using Microsoft.Extensions.Logging;

namespace Lunarite.Network;

// Native ProcNATTest (server op127) needs two relay/test endpoints. The client
// sends [04 00 00 99] to each one; its type-0/subtype-126 receiver consumes
// the public address at bytes 4..7 and port at bytes 8..9 of our reply.
public sealed class P2PNatTestService : IDisposable
{
    private static readonly byte[] Probe = [4, 0, 0, 0x99];
    private static readonly byte[] KeepAlive = [4, 0, 0, 0x9A];
    private readonly ILogger _logger;
    // Fixed ports (HUXLEY_NAT_TEST_PORTS, default 45407,45408) so a host firewall can open them;
    // a port that is taken (a second server or the test suite on one machine) falls back to any.
    private readonly UdpClient[] _listeners = ConfiguredPorts().Select(Listen).ToArray();

    private static ushort[] ConfiguredPorts()
    {
        string[] parts = (Environment.GetEnvironmentVariable("HUXLEY_NAT_TEST_PORTS") ?? "45407,45408").Split(',');
        return parts.Length == 2 && ushort.TryParse(parts[0], out ushort first) && ushort.TryParse(parts[1], out ushort second)
            ? [first, second] : [45407, 45408];
    }

    private static UdpClient Listen(ushort port)
    {
        try { return new UdpClient(new IPEndPoint(IPAddress.Any, port)); }
        catch (SocketException) { return new UdpClient(new IPEndPoint(IPAddress.Any, 0)); }
    }
    private readonly CancellationTokenSource _cancel = new();
    private bool _started;

    public P2PRelayTable Relay { get; } = new();

    public P2PNatTestService(ILogger<P2PNatTestService> logger)
    {
        _logger = logger;
    }

    public ushort[] Ports =>
    [
        checked((ushort)((IPEndPoint)_listeners[0].Client.LocalEndPoint!).Port),
        checked((ushort)((IPEndPoint)_listeners[1].Client.LocalEndPoint!).Port)
    ];

    public NATTest BuildAnnouncement(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork || address.Equals(IPAddress.Any))
            throw new ArgumentException("NAT test endpoint must be a usable IPv4 address.", nameof(address));
        byte[] octets = address.GetAddressBytes();
        int packed = unchecked((int)((uint)octets[0] | ((uint)octets[1] << 8)
            | ((uint)octets[2] << 16) | ((uint)octets[3] << 24)));
        var result = new NATTest();
        foreach (ushort port in Ports) result.Endpoints.Add(new P2PEndpoint(packed, port));
        return result;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        for (int socket = 0; socket < _listeners.Length; socket++)
            _ = ServeAsync(socket, _listeners[socket], _cancel.Token);
        _logger.LogInformation("[P2P] NAT test UDP endpoints listening on ports {0}, {1}.", Ports[0], Ports[1]);
    }

    private async Task ServeAsync(int socket, UdpClient listener, CancellationToken cancel)
    {
        var observedPeers = new HashSet<IPEndPoint>();
        var relayedPeers = new HashSet<IPEndPoint>();
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult request = await listener.ReceiveAsync(cancel);
                if (request.RemoteEndPoint.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (request.Buffer.AsSpan().SequenceEqual(KeepAlive))
                {
                    Relay.NoteClient(socket, request.RemoteEndPoint);
                    continue;
                }
                if (!request.Buffer.AsSpan().SequenceEqual(Probe))
                {
                    if (Relay.Route(socket, request.Buffer, request.RemoteEndPoint) is { } routed)
                    {
                        await listener.SendAsync(routed.Data, routed.To, cancel);
                        if (relayedPeers.Count >= 1024) relayedPeers.Clear();
                        if (relayedPeers.Add(routed.To))
                            _logger.LogInformation("[P2P] relay {0} forwarded {1} to {2}.",
                                ((IPEndPoint)listener.Client.LocalEndPoint!).Port, request.RemoteEndPoint, routed.To);
                    }
                    else if (relayedPeers.Add(request.RemoteEndPoint))
                        _logger.LogWarning("[P2P] relay {0} dropped a {1}-byte frame from {2}.",
                            ((IPEndPoint)listener.Client.LocalEndPoint!).Port, request.Buffer.Length, request.RemoteEndPoint);
                    continue;
                }
                Relay.NoteClient(socket, request.RemoteEndPoint);
                byte[] reply = BuildReply(request.RemoteEndPoint);
                await listener.SendAsync(reply, request.RemoteEndPoint, cancel);
                // The native client sends this probe every 50 ms. Report the first
                // successful exchange per source and test endpoint, not every retry.
                if (observedPeers.Count >= 1024) observedPeers.Clear();
                if (observedPeers.Add(request.RemoteEndPoint))
                    _logger.LogInformation("[P2P] NAT test UDP {0} replied to {1}.",
                        ((IPEndPoint)listener.Client.LocalEndPoint!).Port, request.RemoteEndPoint);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancel.IsCancellationRequested) { break; }
            catch (SocketException ex)
            {
                _logger.LogWarning("[P2P] NAT test UDP socket error: {0}", ex.SocketErrorCode);
            }
        }
    }

    public static byte[] BuildReply(IPEndPoint observed)
    {
        if (observed.Address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("NAT test reply requires IPv4.", nameof(observed));
        byte[] reply = new byte[11];
        BinaryPrimitives.WriteUInt16LittleEndian(reply, 11);
        reply[3] = 126; // type-0 hole-punch frame, subtype NATTypeCheck reply
        observed.Address.GetAddressBytes().CopyTo(reply, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(8), checked((ushort)observed.Port));
        reply[10] = 0; // final-byte type discriminator in FP2PSocket::RecvFrom
        return reply;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        foreach (var listener in _listeners) listener.Dispose();
        _cancel.Dispose();
    }
}
