using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;

namespace Lunarite.World;

// Launches the retail dedicated-server commandlet from the client install; op31 then sends the
// MMO client to that UE3 battle endpoint. One process per BattleServerID.
public sealed class BattleInstanceManager
{
    private readonly ILogger _logger;
    private readonly BattleZoneCatalog _battleZoneCatalog;

    // Rejoin: the player was already a member of this server (a repeated entry after a failed
    // travel); the instance keeps running and the register fee is not charged again.
    public sealed record Admission(int ServerIp, ushort Port, byte[] Key, string LevelName, bool Rejoin = false);

    // AlreadyEntering: this player's earlier request is still launching or admitting (repeated
    // clicks are dropped). Committed: the player belongs to another battlefield it has joined, or
    // was admitted to less than JoinGrace ago.
    public enum EnterResult { Admitted, AlreadyEntering, Committed, Failed }

    private sealed class Instance
    {
        public required int ServerId;
        public required ushort Port;
        public required string LevelName;
        public required Process Process;
        public required DateTime ExpiresAtUtc;

        public HashSet<int> Members { get; } = [];
    }

    private readonly object _lock = new();
    private readonly Dictionary<int, Instance> _instances = [];
    private readonly Dictionary<int, int> _pidServers = [];
    // Entries in progress (PID -> server), instances being launched (server -> outcome) and the
    // ports they hold, so a slow boot runs outside _lock and other players are not blocked.
    private readonly Dictionary<int, int> _entering = [];
    private readonly Dictionary<int, TaskCompletionSource<bool>> _starting = [];
    private readonly HashSet<ushort> _reservedPorts = [];
    // Admission time per member, and the members its instance reported joined (backend op).
    private readonly Dictionary<int, DateTime> _admittedAt = [];
    private readonly HashSet<int> _joined = [];
    // A member who never reaches its instance may pick another battlefield after this long.
    internal static readonly TimeSpan JoinGrace = TimeSpan.FromMinutes(3);
    private const ushort FirstPort = 7788;
    private const ushort LastPort = 7887;
    // How long a new instance gets after match-ready to finish its match reload before players
    // are sent to it (see the start loop).
    private static readonly TimeSpan MatchReloadSettle = TimeSpan.FromSeconds(5);
    // How long a launched instance gets to bind its port and report match-ready. Under Wine the
    // first instance after a boot takes about 95 s (12 s once warm), so Linux waits longer.
    // HUXLEY_BATTLE_START_SECONDS overrides.
    private static TimeSpan StartTimeout =>
        TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("HUXLEY_BATTLE_START_SECONDS"), out int seconds)
            && seconds > 0 ? seconds : OperatingSystem.IsWindows() ? 30 : 120);
    private readonly Timer _expiryTimer;

    private readonly BattleServerManager _battleServerManager;

    public BattleInstanceManager(ILogger<BattleInstanceManager> logger, BattleZoneCatalog battleZoneCatalog,
        BattleServerManager battleServerManager)
    {
        _logger = logger;
        _battleZoneCatalog = battleZoneCatalog;
        _battleServerManager = battleServerManager;
        _expiryTimer = new(_ => SweepExpired(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll();
    }

    public EnterResult TryEnter(int pid, int serverId, out Admission? admission)
    {
        admission = null;
        if (pid <= 0) return EnterResult.Failed;
        var zone = _battleZoneCatalog.GetByServerId(serverId);
        if (zone == null || !zone.Enabled || !MapAvailable(zone.LevelName))
        {
            _logger.LogWarning("[BattleInstance] PID {0} refused for server {1}: {2}.", pid, serverId,
                zone == null ? "no such battle zone" : !zone.Enabled ? $"{zone.LevelName} is disabled"
                    : $"{zone.LevelName}.war is not in the client");
            return EnterResult.Failed;
        }

        TaskCompletionSource<bool> outcome;
        bool launch = false;
        ushort port = 0;
        lock (_lock)
        {
            PruneExitedLocked();
            if (_entering.ContainsKey(pid)) return EnterResult.AlreadyEntering;
            int current = _pidServers.GetValueOrDefault(pid);
            if (current != 0 && current != serverId)
            {
                if (_joined.Contains(pid) || DateTime.UtcNow - _admittedAt.GetValueOrDefault(pid) < JoinGrace)
                    return EnterResult.Committed;
                LeaveLocked(pid);
            }
            // A repeated entry keeps the player's place: leaving first would stop a solo
            // player's instance only to launch it again.
            if (_instances.TryGetValue(serverId, out var running))
            {
                admission = AdmitLocked(pid, zone, running, rejoin: current == serverId);
                return EnterResult.Admitted;
            }
            _entering[pid] = serverId;
            if (_starting.TryGetValue(serverId, out var pending)) outcome = pending;
            else
            {
                port = FreePortLocked();
                if (port == 0)
                {
                    _entering.Remove(pid);
                    return EnterResult.Failed;
                }
                _reservedPorts.Add(port);
                outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _starting[serverId] = outcome;
                launch = true;
            }
        }

        try
        {
            if (launch)
            {
                bool started = false;
                Instance? instance = null;
                try { started = TryStart(zone, port, out instance); }
                finally
                {
                    lock (_lock)
                    {
                        _starting.Remove(serverId);
                        _reservedPorts.Remove(port);
                        if (started) _instances[serverId] = instance!;
                    }
                    outcome.SetResult(started);
                }
            }
            else outcome.Task.Wait();
            lock (_lock)
            {
                if (!_instances.TryGetValue(serverId, out var instance)) return EnterResult.Failed;
                admission = AdmitLocked(pid, zone, instance, rejoin: false);
                return EnterResult.Admitted;
            }
        }
        finally
        {
            lock (_lock) _entering.Remove(pid);
        }
    }

    private Admission AdmitLocked(int pid, BattleZoneCatalog.Zone zone, Instance instance, bool rejoin)
    {
        DateTime requestedExpiry = DateTime.UtcNow.AddMinutes(Math.Max(5, (int)zone.TimeLimitMinutes) + 10);
        if (requestedExpiry > instance.ExpiresAtUtc) instance.ExpiresAtUtc = requestedExpiry;
        instance.Members.Add(pid);
        _pidServers[pid] = instance.ServerId;
        if (!rejoin)
        {
            _admittedAt[pid] = DateTime.UtcNow;
            _joined.Remove(pid);
        }
        byte[] key = RandomNumberGenerator.GetBytes(16);
        return new Admission(PackedHostIp(), instance.Port, key, instance.LevelName, rejoin);
    }

    // The instance reported the player in (backend PlayerJoined): the choice is now binding.
    public void MarkJoined(int pid)
    {
        lock (_lock)
            if (_pidServers.ContainsKey(pid)) _joined.Add(pid);
    }

    // A battle is in progress on this server while its dedicated instance has members.
    public bool IsRunning(int serverId)
    {
        lock (_lock) return _instances.TryGetValue(serverId, out var instance) && instance.Members.Count != 0;
    }

    public int ServerOf(int pid)
    {
        lock (_lock) return _pidServers.GetValueOrDefault(pid);
    }

    public bool IsInBattle(int pid)
    {
        lock (_lock) return _pidServers.ContainsKey(pid);
    }

    public bool IsMemberOf(int pid, int serverId)
    {
        lock (_lock) return _pidServers.GetValueOrDefault(pid) == serverId && _instances.ContainsKey(serverId);
    }

    public void Leave(int pid)
    {
        if (pid <= 0) return;
        lock (_lock) LeaveLocked(pid);
    }

    private void ForgetMemberLocked(int pid)
    {
        _pidServers.Remove(pid);
        _admittedAt.Remove(pid);
        _joined.Remove(pid);
    }

    private void LeaveLocked(int pid)
    {
        int serverId = _pidServers.GetValueOrDefault(pid);
        ForgetMemberLocked(pid);
        if (serverId == 0 || !_instances.TryGetValue(serverId, out var instance))
            return;
        instance.Members.Remove(pid);
        if (instance.Members.Count != 0) return;
        _instances.Remove(serverId);
        StopInstance(instance);
        _logger.LogInformation("[BattleInstance] stopped empty server {0} ({1}) on port {2}.",
            serverId, instance.LevelName, instance.Port);
    }

    // Runs outside _lock: the boot takes 10-120 s and must not hold up other players' entries.
    private bool TryStart(BattleZoneCatalog.Zone zone, ushort port, out Instance instance)
    {
        instance = null!;
        string root = ClientFolder.Root;
        string executable = ClientFolder.Resolve(root, @"Binaries\HuxleyMMOGame.exe");
        if (!File.Exists(executable)) return false;

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // On Linux the Windows exe runs under Wine: HUXLEY_WINE names the launcher. The dedicated
        // commandlet still creates a Direct3D device and fails without a display ("Please install
        // DirectX 9.0c"), so the default gives it a virtual one (xvfb-run).
        if (!OperatingSystem.IsWindows())
        {
            string[] wine = (Environment.GetEnvironmentVariable("HUXLEY_WINE") is { Length: > 0 } configured
                ? configured : "xvfb-run -a wine").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            start.FileName = wine[0];
            foreach (string argument in wine.Skip(1)) start.ArgumentList.Add(argument);
            start.ArgumentList.Add(executable);
        }
        // UHuxleyServerCommandlet::Main constructs CServerTcpLink; the generic Server
        // commandlet does not.
        start.ArgumentList.Add("HuxleyServer");
        start.ArgumentList.Add(zone.LevelName);
        start.ArgumentList.Add($"-port={port}");
        // HuxleyEngine only constructs CServerTcpLink when SERVERS is present; the link reads
        // these command-line keys and connects the instance to the backend result service.
        start.ArgumentList.Add("SERVERS=1");
        start.ArgumentList.Add($"BattleIndex={zone.BattleServerId}");
        start.ArgumentList.Add($"BattleIP={BattleAdvertisedIp()}");
        start.ArgumentList.Add($"BattlePort={port}");
        // CServerTcpLink::Connect reads GameIP/GamePort (config section
        // [BattleServer]); BattleIP/BattlePort above describe this instance.
        start.ArgumentList.Add($"GameIP={BattleBackendIp()}");
        start.ArgumentList.Add($"GamePort={BattleBackendPort()}");
        start.ArgumentList.Add("-LANGUAGE=INT");
        // No -LOG: it opens a console window for every battle. UE3 still writes the log file
        // (HuxleyMMOGame\Logs\Launch_Server.log) without it.
        start.ArgumentList.Add("-nosound");
        // Flush each line, so a join that fails before the instance is stopped is still in the log
        // ("Level server received: HELLO ...", the reply it got).
        start.ArgumentList.Add("-FORCELOGFLUSH");
        start.ArgumentList.Add("-unattended");
        if (WriteServerEngineIni(root) is { } engineIni)
            start.ArgumentList.Add($"-ENGINEINI={ClientFolder.GamePath(engineIni)}");
        // The backend verifies (op6) only the BattleIndex/BattlePort pairs launched here.
        _battleServerManager.ExpectServer(zone.BattleServerId, port);
        Process? process;
        try { process = Process.Start(start); }
        catch (Exception ex)
        {
            _battleServerManager.ForgetServer(zone.BattleServerId, port);
            _logger.LogWarning("[BattleInstance] failed to launch {0}: {1}", zone.LevelName, ex.Message);
            return false;
        }
        if (process == null)
        {
            _battleServerManager.ForgetServer(zone.BattleServerId, port);
            return false;
        }

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < StartTimeout)
        {
            if (process.HasExited) break;
            if (IsUdpListening(port) && _battleServerManager.IsReady(zone.BattleServerId, port))
            {
                // The instance reports match-ready and then reloads its level for the match (START
                // MATCH, "Server switch level"). LoadMap (0x111A45A0) clears the MMO link global
                // (dword_1239A7A0) until the new world is up, and a HELLO arriving in that window
                // takes the stock version check: the client's Huxley VER=3 gets "UPGRADE MINVER=3077
                // VER=3240" ("Server's version is outdated"). Fast remote clients hit it (live
                // 2026-11-03, PID 14); the reload takes about a second.
                Thread.Sleep(MatchReloadSettle);
                if (process.HasExited) break;
                instance = new Instance
                {
                    ServerId = zone.BattleServerId, Port = port, LevelName = zone.LevelName, Process = process,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(Math.Max(5, (int)zone.TimeLimitMinutes) + 10)
                };
                _logger.LogInformation("[BattleInstance] started server {0} ({1}) on UDP {2}, PID {3}.",
                    zone.BattleServerId, zone.LevelName, port, process.Id);
                return true;
            }
            Thread.Sleep(100);
        }
        Stop(process);
        _battleServerManager.ForgetServer(zone.BattleServerId, port);
        _logger.LogWarning("[BattleInstance] {0} failed to bind UDP {1} and reach backend match-ready state within {2} seconds.",
            zone.LevelName, port, (int)StartTimeout.TotalSeconds);
        return false;
    }

    // HuxleyGame.HuxleyP2PNetDriver's LowLevelDestroy (0x1115E970) never closes its socket, which
    // in P2P mode belongs to the relay. After backend op3 triggers ServerTravel, the old socket
    // stays bound (SO_REUSEADDR) and swallows client datagrams ("Connection timed out after 200
    // seconds"). The dedicated server therefore uses the stock IpDrv.TcpNetDriver, which matches
    // the client after op31 disarms its P2P socket. Rewritten per launch because appInit
    // regenerates an ENGINEINI older than DefaultEngine.ini.
    private string? WriteServerEngineIni(string root)
    {
        string source = ClientFolder.Resolve(root, @"HuxleyMMOGame\Config\HuxleyMMOEngine.ini");
        try
        {
            // Next to the client's own ini, not under the server's build folder: UE3 splits the
            // -ENGINEINI= argument at spaces (e.g. "C:\Huxley Server\bin\..."), loads a truncated path,
            // and falls back to the client ini's HuxleyP2PNetDriver, whose socket survives the match
            // reload and swallows every HELLO (live Outback joins, 2026-11-03).
            string path = Path.Combine(Path.GetDirectoryName(source)!, "HuxleyServerEngine.ini");
            if (path.Contains(' '))
                _logger.LogWarning("[BattleInstance] the dedicated engine ini path {0} contains a space; the instance will fall back to the P2P driver.", path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildServerEngineIni(File.ReadAllText(source, System.Text.Encoding.Latin1)),
                System.Text.Encoding.Latin1);
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[BattleInstance] could not write the dedicated engine ini from {0}: {1}", source, ex.Message);
            return null;
        }
    }

    public static string BuildServerEngineIni(string clientEngineIni)
    {
        var lines = clientEngineIni.Split('\n').ToList();
        string section = "";
        bool replaced = false;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].TrimEnd('\r').Trim();
            if (line.StartsWith('[')) { section = line; continue; }
            if (section.Equals("[Engine.Engine]", StringComparison.OrdinalIgnoreCase)
                && line.StartsWith("NetworkDevice=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "NetworkDevice=IpDrv.TcpNetDriver" + (lines[i].EndsWith('\r') ? "\r" : "");
                replaced = true;
            }
        }
        if (!replaced)
        {
            int at = lines.FindIndex(x => x.TrimEnd('\r').Trim().Equals("[Engine.Engine]", StringComparison.OrdinalIgnoreCase));
            if (at < 0) { lines.Add("[Engine.Engine]\r"); at = lines.Count - 1; }
            lines.Insert(at + 1, "NetworkDevice=IpDrv.TcpNetDriver\r");
        }
        return string.Join('\n', lines);
    }

    private ushort FreePortLocked()
    {
        var active = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners()
            .Select(x => x.Port).ToHashSet();
        foreach (ushort port in Enumerable.Range(FirstPort, LastPort - FirstPort + 1).Select(x => (ushort)x))
            if (!active.Contains(port) && !_reservedPorts.Contains(port) && _instances.Values.All(x => x.Port != port))
                return port;
        return 0;
    }

    private static bool IsUdpListening(ushort port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(x => x.Port == port);

    internal Func<string, bool> MapAvailable = MapExists;

    private static bool MapExists(string levelName)
    {
        string maps = ClientFolder.Resolve(@"HuxleyGame\Content\Maps");
        return Directory.Exists(maps) && Directory.EnumerateFiles(maps, levelName + ".war",
            ClientFolder.AnyCase).Any();
    }

    private static int PackedHostIp()
    {
        string value = Environment.GetEnvironmentVariable("BATTLE_HOST_IP") ?? "127.0.0.1";
        if (!IPAddress.TryParse(value, out var address)) address = IPAddress.Loopback;
        byte[] bytes = address.MapToIPv4().GetAddressBytes();
        return bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24;
    }

    private static string BattleBackendIp() =>
        Environment.GetEnvironmentVariable("BATTLE_BACKEND_IP") ?? "127.0.0.1";

    private static string BattleAdvertisedIp() =>
        Environment.GetEnvironmentVariable("BATTLE_HOST_IP") ?? "127.0.0.1";

    private static int BattleBackendPort() =>
        int.TryParse(Environment.GetEnvironmentVariable("BATTLE_BACKEND_PORT"), out int port)
            && port is > 0 and <= ushort.MaxValue ? port : 55902;

    private void PruneExitedLocked()
    {
        foreach (var pair in _instances.Where(x => x.Value.Process.HasExited).ToList())
        {
            foreach (int pid in pair.Value.Members) ForgetMemberLocked(pid);
            _instances.Remove(pair.Key);
            _battleServerManager.ForgetServer(pair.Value.ServerId, pair.Value.Port);
        }
    }

    private void SweepExpired()
    {
        lock (_lock)
        {
            PruneExitedLocked();
            DateTime now = DateTime.UtcNow;
            foreach (var pair in _instances.Where(x => x.Value.ExpiresAtUtc <= now).ToList())
            {
                foreach (int pid in pair.Value.Members) ForgetMemberLocked(pid);
                _instances.Remove(pair.Key);
                StopInstance(pair.Value);
                _logger.LogInformation("[BattleInstance] expired server {0} ({1}) on port {2}.",
                    pair.Key, pair.Value.LevelName, pair.Value.Port);
            }
        }
    }

    private void StopAll()
    {
        lock (_lock)
        {
            foreach (var instance in _instances.Values) StopInstance(instance);
            _instances.Clear();
            _pidServers.Clear();
            _admittedAt.Clear();
            _joined.Clear();
        }
    }

    // Test seam: registers a running instance without launching the dedicated commandlet.
    internal void AddInstanceForTest(int serverId, ushort port, string levelName, Process process, params int[] members)
    {
        lock (_lock)
        {
            var instance = new Instance
            {
                ServerId = serverId, Port = port, LevelName = levelName, Process = process,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            };
            foreach (int pid in members)
            {
                instance.Members.Add(pid);
                _pidServers[pid] = serverId;
                _admittedAt[pid] = DateTime.UtcNow;
            }
            _instances[serverId] = instance;
            _battleServerManager.ExpectServer(serverId, port);
        }
    }

    // Test seams: an entry still launching, and an admission made `age` ago.
    internal void BeginEntryForTest(int pid, int serverId)
    {
        lock (_lock) _entering[pid] = serverId;
    }

    internal void EndEntryForTest(int pid)
    {
        lock (_lock) _entering.Remove(pid);
    }

    internal void AgeAdmissionForTest(int pid, TimeSpan age)
    {
        lock (_lock)
            if (_admittedAt.ContainsKey(pid)) _admittedAt[pid] = DateTime.UtcNow - age;
    }

    private void StopInstance(Instance instance)
    {
        _battleServerManager.ForgetServer(instance.ServerId, instance.Port);
        Stop(instance.Process);
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { }
        process.Dispose();
    }
}
