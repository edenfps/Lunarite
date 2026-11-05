using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class BattleServerManager
{
    private readonly ILogger _logger;

    private readonly PlayerStore _playerStore;
    private readonly TransmitStore _transmitStore;

    internal const byte BackendVersion = 19;

    internal readonly record struct VerifiedServer(byte Index, ushort Port, bool Ready, int RemainingSeconds);

    private readonly ConcurrentDictionary<int, VerifiedServer> _verifiedServers = [];
    private readonly ConcurrentDictionary<int, Client> _serverClients = [];
    private readonly ConcurrentDictionary<(int Server, int Pid), PendingPermission> _pendingPermissions = [];
    // (BattleIndex, BattlePort) of the instances BattleInstanceManager launched; op6 verifies only these.
    private readonly ConcurrentDictionary<(int Index, ushort Port), byte> _launchedServers = [];
    // Per backend link: the PIDs it acknowledged through op9, the only ones its op3/op4 may report.
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, byte>> _linkPlayers = [];
    // Per backend link: op0's GRI.bDominatingVictory for the match whose op3 results follow.
    private readonly ConcurrentDictionary<int, bool> _dominatingVictories = [];

    public BattleServerManager(ILogger<BattleServerManager> logger, PlayerStore playerStore, TransmitStore transmitStore)
    {
        _logger = logger;
        _playerStore = playerStore;
        _transmitStore = transmitStore;
    }

    internal sealed class PendingPermission
    {
        public ManualResetEventSlim Signal { get; } = new(false);

        public byte Answer;
    }

    public bool IsVerified(int battleIndex, ushort port) =>
        _verifiedServers.Values.Any(x => x.Index == battleIndex && x.Port == port);

    public bool IsReady(int battleIndex, ushort port) =>
        _verifiedServers.Values.Any(x => x.Index == battleIndex && x.Port == port && x.Ready);

    public bool TryRegisterPlayer(int battleIndex, BattleBackendPermissionRegister packet)
    {
        var connection = _verifiedServers.FirstOrDefault(x => x.Value.Index == battleIndex && x.Value.Ready);
        if (connection.Equals(default(KeyValuePair<int, VerifiedServer>))
            || !_serverClients.TryGetValue(connection.Key, out var client)) return false;
        var key = (battleIndex, packet.Pid);
        var pending = new PendingPermission();
        if (!_pendingPermissions.TryAdd(key, pending)) return false;
        try
        {
            client.SendPacket(packet);
            if (!pending.Signal.Wait(TimeSpan.FromSeconds(3)) || pending.Answer == 0) return false;
            RecordLinkPlayer(connection.Key, packet.Pid);
            return true;
        }
        finally
        {
            _pendingPermissions.TryRemove(key, out _);
            pending.Signal.Dispose();
        }
    }

    public const int LunariteItem = 500001;

    public const int MedalOfValorItem = 609101; // HuxleyMMOGame.ini BMedalItemIndex

    // A report's Lunarite already includes the zone reward; cap it at the largest table reward
    // plus a full carry load (LunarOreAmountMax=30) and the dominating-victory bonus. The table's
    // Medals of Valor get that bonus here: HuxleyMultiTeamGame pays WinTeamMedalCount+1 and
    // LoseTeamMedalCount-1 on bDominatingVictory.
    public IReadOnlyList<(int Item, int Count)> BattleFieldRewardItems(
        BattleZoneCatalog.Zone zone, byte species, bool won, int reportedLunarOre, bool dominatingVictory = false)
    {
        var table = zone.RewardsFor(species, won);
        if (table.Count == 0) return [];
        int maxTableLunarite = new[] { zone.SapiWinRewards, zone.SapiLoseRewards, zone.AlterWinRewards,
                zone.AlterLoseRewards }
            .SelectMany(x => x).Where(x => x.Item == LunariteItem).Select(x => x.Count).DefaultIfEmpty(0).Max();
        int lunarite = Math.Clamp(reportedLunarOre, 0, maxTableLunarite + 30 + 1);
        var items = table.Where(x => x.Item != LunariteItem)
            .Select(x => x.Item == MedalOfValorItem && dominatingVictory
                ? (Item: x.Item, Count: Math.Max(0, x.Count + (won ? 1 : -1))) : x)
            .Where(x => x.Count > 0).ToList();
        if (lunarite > 0) items.Add((LunariteItem, lunarite));
        return items;
    }

    // Retail battle fields pay Medals of Valor and Certificates of Battle from the zone's
    // Sapi/AlterWin/LoseRewardItems (they are not in the result packet) and Lunarite from the report.
    // HuxleyMultiTeamGame always credits them, so what the inventory cannot hold arrives by mail.
    internal void GrantBattleFieldRewards(BattleResultRequest report, BattleZoneCatalog.Zone zone,
        bool dominatingVictory = false)
    {
        var items = BattleFieldRewardItems(zone, report.Species, report.Won, report.LunarOre, dominatingVictory);
        if (items.Count == 0) return;
        var inserted = new List<PlayerStore.GenericItem>();
        var mailed = new List<(int Item, int Count)>();
        foreach (var item in items)
            if (_playerStore.TryAddQuestItems(report.Pid, [item], out var itemInserted)) inserted.AddRange(itemInserted);
            else mailed.Add(item);
        if (inserted.Count > 0) ItemManager.PublishLiveInventoryInserts(report.Pid, inserted);
        if (mailed.Count > 0)
        {
            // TRANSMITTYPE_SYSTEM; a reward list has at most three items, the mail attachment limit.
            // Built as acquired items so the medals keep BIND_WHEN_PICKUP through the mail.
            _transmitStore.Add(report.Pid, 0, 4, "System", "Battlefield rewards",
                "Your inventory was full, so these battlefield rewards were sent by mail.",
                attachments: mailed.Select(x =>
                {
                    var item = _playerStore.CreateAcquiredItem(x.Item, x.Count);
                    item.InventoryIndex = InventoryType.Main;
                    return item;
                }));
            TransmitManager.NotifyTransmitArrival(report.Pid, 4);
            _logger.LogWarning("[BattleBackend] PID {0} inventory full: mailed battle-field rewards {1}.",
                report.Pid, string.Join(", ", mailed.Select(x => $"{x.Item}x{x.Count}")));
        }
        _logger.LogInformation("[BattleBackend] PID {0} battle-field {1} rewards: {2}.", report.Pid,
            report.Won ? "win" : "loss", string.Join(", ", items.Select(x => $"{x.Item}x{x.Count}")));
    }

    // Seconds left in the match on a battle server, from its op12 time reports (0 if unknown).
    public int RemainingSecondsFor(int serverId) =>
        _verifiedServers.Values.Where(x => x.Index == serverId).Select(x => x.RemainingSeconds)
            .DefaultIfEmpty(0).Max();

    internal bool TryGetServer(int clientId, out VerifiedServer server) => _verifiedServers.TryGetValue(clientId, out server);

    internal void SetServer(int clientId, VerifiedServer server) => _verifiedServers[clientId] = server;

    internal bool IsServerLink(int clientId) => _verifiedServers.ContainsKey(clientId);

    internal void SetServerClient(int clientId, Client client) => _serverClients[clientId] = client;

    internal void RemoveLink(int clientId)
    {
        _verifiedServers.TryRemove(clientId, out _);
        _serverClients.TryRemove(clientId, out _);
        _linkPlayers.TryRemove(clientId, out _);
        _dominatingVictories.TryRemove(clientId, out _);
    }

    internal void ExpectServer(int battleIndex, ushort port) => _launchedServers[(battleIndex, port)] = 0;

    internal void ForgetServer(int battleIndex, ushort port) => _launchedServers.TryRemove((battleIndex, port), out _);

    internal bool IsLaunched(int battleIndex, ushort port) => _launchedServers.ContainsKey((battleIndex, port));

    internal void RecordLinkPlayer(int clientId, int pid) =>
        _linkPlayers.GetOrAdd(clientId, _ => new())[pid] = 0;

    // A verified link may only report op3 results and op4 item use for players it admitted.
    internal bool IsLinkPlayer(int clientId, int pid) =>
        IsServerLink(clientId) && _linkPlayers.TryGetValue(clientId, out var pids) && pids.ContainsKey(pid);

    internal void SetDominatingVictory(int clientId, bool dominating) => _dominatingVictories[clientId] = dominating;

    internal bool DominatingVictoryOf(int clientId) => _dominatingVictories.GetValueOrDefault(clientId);

    internal bool TryGetPendingPermission((int Server, int Pid) key, out PendingPermission pending) =>
        _pendingPermissions.TryGetValue(key, out pending!);
}
