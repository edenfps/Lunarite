using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Microsoft.Extensions.Logging;

namespace Lunarite.Network;

public class Server : TcpListener
{
    public HandlerType Type { get; }

    public ILoggerFactory LoggerFactory { get; }

    private readonly OnHandlePacket _handler;

    public delegate void OnHandlePacket(Client client, ReadOnlySpan<byte> data);

    private static int _uniqueClientId;
    private readonly ILogger _logger;
    private readonly List<Client> _clients = [];
    private readonly object _clientsLock = new();

    public Server(int port, HandlerType type, OnHandlePacket handler, ILoggerFactory loggerFactory)
        : base(ListenAddress(type, Environment.GetEnvironmentVariable("BATTLE_BACKEND_IP")), port)
    {
        _handler = handler;
        Type = type;
        LoggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Server>();

    }

    // The battle backend (55902) is unauthenticated plain TCP and only the dedicated instances
    // BattleInstanceManager launches on this machine connect to it, at BATTLE_BACKEND_IP
    // (127.0.0.1 by default). It listens on loopback unless that address is set to another one.
    public static IPAddress ListenAddress(HandlerType type, string? battleBackendIp) =>
        type != HandlerType.Battle ? IPAddress.Any
            : battleBackendIp == null || (IPAddress.TryParse(battleBackendIp, out var address) && IPAddress.IsLoopback(address))
                ? IPAddress.Loopback : IPAddress.Any;

    public new void Start()
    {
        base.Start();

        BeginAcceptTcpClient(OnAccept, this);
    }

    private void OnAccept(IAsyncResult asyncResult)
    {
        TcpClient tcpClient;
        try
        {
            tcpClient = EndAcceptTcpClient(asyncResult);
        }
        catch (ObjectDisposedException) { return; } // normal listener shutdown with an accept pending
        // TcpListener.Stop disposes the socket before clearing Active, so a pending accept can fail
        // with OperationAborted (995) while Active is still true; that abort always means shutdown.
        catch (SocketException ex) when (!Active || ex.SocketErrorCode == SocketError.OperationAborted) { return; }

        EndPoint? remote = null;
        try { remote = tcpClient.Client.RemoteEndPoint; }
        catch (ObjectDisposedException) { /* peer closed during accept */ }

        // The constructor starts receiving, so a quick client can log in and disconnect before
        // the add below. Its removal then runs first, and the stale entry would keep holding
        // the account (every later login answers SameUser). Disconnect marks the client before
        // removing it, so re-checking after the add catches that order.
        var client = new Client(Interlocked.Increment(ref _uniqueClientId) - 1, this, tcpClient);
        lock (_clientsLock)
        {
            _clients.Add(client);
            if (!client.IsConnected) _clients.Remove(client);
        }
        _logger.LogInformation("[{Type}][{Client}] Accepted client connection from {Remote}.", Type, client.Id, remote);

        try { BeginAcceptTcpClient(OnAccept, this); }
        catch (ObjectDisposedException) { /* normal listener shutdown */ }
        catch (InvalidOperationException) when (!Active) { /* stopped after this accept completed */ }
        catch (SocketException ex) when (!Active || ex.SocketErrorCode == SocketError.OperationAborted)
        {
            /* normal listener shutdown */
        }
    }

    public Client[] Clients
    {
        get { lock (_clientsLock) return _clients.ToArray(); }
    }

    public void HandlePacket(Client client, ReadOnlySpan<byte> data)
    {
        _handler(client, data);
    }

    public void DisconnectClient(Client client)
    {
        string remoteAddress;
        try { remoteAddress = client.IPAddress.ToString(); }
        catch (ObjectDisposedException) { remoteAddress = "closed socket"; }
        _logger.LogInformation("[{Type}][{Client}] Client connection lost or closed from {Remote}.", Type, client.Id,
            remoteAddress);

        lock (_clientsLock) _clients.Remove(client);
        if (Type == HandlerType.Game) Lunarite.Handlers.DisconnectHandler.HandleDisconnect(client);
        else if (Type == HandlerType.Battle) Lunarite.Handlers.BattleBackend.OnDisconnect(client);
    }
}
