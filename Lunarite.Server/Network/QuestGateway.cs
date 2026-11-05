using System;
using System.Collections.Generic;
using System.Net;

using Microsoft.Extensions.Logging;

namespace Lunarite.Network;

// One session per host PID, so parties running the same quest concurrently share no ConnData,
// sockets, sequences or account link.
public sealed class QuestGateway
{
    // The gateway is experimental and missions play fully without it, so it runs only when
    // QUEST_GATEWAY_DISABLED=0 asks for it.
    public static bool Enabled => Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED") == "0";

    private readonly ILoggerFactory _loggerFactory;
    private readonly object _sessionsLock = new();
    private readonly Dictionary<int, QuestGatewaySession> _sessions = [];
    private readonly QuestGatewaySession _diagnostic;

    public QuestGateway(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _diagnostic = new QuestGatewaySession(loggerFactory.CreateLogger<QuestGatewaySession>());
    }

    public void ConfigureRoom(int pid, byte[] connData, Client? client = null,
        IPAddress? hostAddress = null, int ownerPid = 0, Action<int, int>? roomReady = null,
        ushort hostPort = 7777)
    {
        if (ownerPid <= 0) throw new ArgumentOutOfRangeException(nameof(ownerPid));
        QuestGatewaySession? prior;
        var session = new QuestGatewaySession(_loggerFactory.CreateLogger<QuestGatewaySession>());
        session.ConfigureRoom(pid, connData, client, hostAddress, ownerPid, roomReady, hostPort);
        lock (_sessionsLock)
        {
            _sessions.Remove(ownerPid, out prior);
            _sessions[ownerPid] = session;
        }
        prior?.CancelHandshake(ownerPid);
    }

    public void StartHandshake(int ownerPid)
    {
        QuestGatewaySession? session;
        lock (_sessionsLock) _sessions.TryGetValue(ownerPid, out session);
        if (session == null)
            throw new InvalidOperationException($"Quest gateway host PID {ownerPid} is not configured.");
        session.StartHandshake();
    }

    public bool CancelHandshake(int ownerPid)
    {
        QuestGatewaySession? session;
        lock (_sessionsLock)
        {
            if (!_sessions.Remove(ownerPid, out session)) return false;
        }
        return session.CancelHandshake(ownerPid);
    }

    public IPEndPoint? TargetEndpoint(int ownerPid)
    {
        lock (_sessionsLock)
            return _sessions.TryGetValue(ownerPid, out var session) ? session.TargetEndpoint : null;
    }

    internal int ActiveSessionCount
    {
        get { lock (_sessionsLock) return _sessions.Count; }
    }

    public void StartTest() => _diagnostic.StartTest();

    public void StartCapture() => _diagnostic.StartCapture();

    public void StartProxy() => _diagnostic.StartProxy();

    public void SendPingProbe(uint tick) => _diagnostic.SendPingProbe(tick);
}
