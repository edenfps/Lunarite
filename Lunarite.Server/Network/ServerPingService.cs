using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Lunarite.Network;

// Server-select ping (the antenna bars beside each server). Native OnServerList adds a
// PingType-3 PingClient for every listed server at its IP and the client INI's
// HuxleySubsystem.m_nServerPingPort (56983), then sends the 12-byte P2P ping
// [Ack=0][Key4][AddrSeq][PingType][Tick4][4]. ProcessUDPClientPing (0x111D6E50) answers a
// request by echoing it with Ack=1; an echoed reply turns its round trip into PingGrade
// 0..3 (bars 1..4). Without a reply the list shows PINGINFO_UNKNOWN ("?").
public sealed class ServerPingService : IDisposable
{
    public const int Size = 12;
    private const byte PingFrame = 4;

    private readonly ILogger _logger;
    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _cancel = new();

    public ServerPingService(ILogger logger, ushort port)
    {
        _logger = logger;
        _listener = new UdpClient(new IPEndPoint(IPAddress.Any, port));
    }

    // Other small requests on this port (DiscordPresence); null answers nothing.
    public Func<byte[], IPEndPoint, byte[]?>? Query { get; set; }

    public ushort Port => checked((ushort)((IPEndPoint)_listener.Client.LocalEndPoint!).Port);

    // HUXLEY_SERVER_PING_PORT, default 56983 (the shipped HuxleyMMOGame.ini); 0 turns it off.
    public static ServerPingService? StartFromEnvironment(ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ServerPing");
        string? setting = Environment.GetEnvironmentVariable("HUXLEY_SERVER_PING_PORT");
        ushort port = 56983;
        if (!string.IsNullOrWhiteSpace(setting) && !ushort.TryParse(setting, out port))
        {
            logger.LogWarning("[ServerPing] HUXLEY_SERVER_PING_PORT '{0}' is not a port; using 56983.", setting);
            port = 56983;
        }
        if (port == 0) return null;
        try
        {
            var service = new ServerPingService(logger, port);
            service.Start();
            return service;
        }
        catch (SocketException ex)
        {
            logger.LogWarning("[ServerPing] UDP {0} unavailable ({1}); server list pings show '?'.", port, ex.SocketErrorCode);
            return null;
        }
    }

    public void Start()
    {
        _ = ServeAsync(_cancel.Token);
        _logger.LogInformation("[ServerPing] answering server-list pings on UDP {0}.", Port);
    }

    // The reply to a ping request, or null for anything else (replies, other P2P frames).
    public static byte[]? BuildReply(ReadOnlySpan<byte> request)
    {
        if (request.Length != Size || request[0] != 0 || request[Size - 1] != PingFrame) return null;
        byte[] reply = request.ToArray();
        reply[0] = 1;
        return reply;
    }

    private async Task ServeAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult request = await _listener.ReceiveAsync(cancel);
                if ((BuildReply(request.Buffer) ?? Query?.Invoke(request.Buffer, request.RemoteEndPoint)) is { } reply)
                    await _listener.SendAsync(reply, request.RemoteEndPoint, cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancel.IsCancellationRequested) { break; }
            // An ICMP port-unreachable for an earlier reply surfaces here on Windows; keep serving.
            catch (SocketException) { }
        }
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _listener.Dispose();
        _cancel.Dispose();
    }
}
