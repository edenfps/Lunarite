using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class CityManager
{
    private readonly ILogger _logger;
    private readonly QuestManager _questManager;
    private readonly SquadManager _squadManager;

    private readonly CharacterStore _characterStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly PlayerManager _playerManager;
    private readonly PlayerStore _playerStore;

    // PID -> city location before the first !goto, for !back (in memory; survives city relinks).
    private const long UnstuckCooldownMs = 30_000;
    private readonly object _transportEntryLock = new();

    private readonly BattleInstanceManager _battleInstanceManager;
    private readonly CityTransportCatalog _cityTransportCatalog;
    private readonly CityZoneCatalog _cityZoneCatalog;

    private readonly QuestRoomMembers _questRoomMembers;

    public CityManager(ILogger<CityManager> logger, CharacterStore characterStore,
        DurativeItemStore durativeItemStore, PlayerManager playerManager, PlayerStore playerStore, QuestManager questManager, SquadManager squadManager, BattleInstanceManager battleInstanceManager, CityTransportCatalog cityTransportCatalog, CityZoneCatalog cityZoneCatalog, QuestRoomMembers questRoomMembers)
    {
        _questRoomMembers = questRoomMembers;
        _logger = logger;
        _characterStore = characterStore;
        _durativeItemStore = durativeItemStore;
        _playerManager = playerManager;
        _playerStore = playerStore;
        _questManager = questManager;
        _squadManager = squadManager;
        _battleInstanceManager = battleInstanceManager;
        _cityTransportCatalog = cityTransportCatalog;
        _cityZoneCatalog = cityZoneCatalog;
    }

    // HuxleyMapMenu_City clamps ordinary distance pricing to MinimumTeleportCharge=990.
    // A price of -1 is the script's pass branch; HuxleyPlayerWrapper.HasCityWarpPass
    // recognizes active DurativeItem city group 15. Result 2 is the native insufficient-money case.
    public byte ApplyCityWarpCost(int pid, int price)
    {
        if (pid <= 0) return 1;
        if (price == -1)
            return _durativeItemStore.Active(pid).Any(x => x.CityItemGroup == 15) ? (byte)0 : (byte)1;
        if (price < 990) return 1;
        return _playerStore.TrySpendMoney(pid, price) ? (byte)0 : (byte)2;
    }

    internal static string? DirectoryKeyword(Client client, byte kind, string? newSearch)
    {
        var searches = client.Session.DirectorySearches;
        if (newSearch != null)
        {
            if (newSearch.Length == 0) searches.TryRemove(kind, out _);
            else searches[kind] = newSearch;
        }
        return searches.TryGetValue(kind, out string? current) ? current : null;
    }

    // HuxleyCityTransportData.UseFee is charged here. Native results are:
    // 0 OK, 1 ERROR, 2 NOTFOUND, 3 ENOUGHMONEY, 4 ALREADYRODE.
    public byte TryEnterTransport(int pid, byte routeIndex, bool alreadyRiding)
    {
        if (pid <= 0) return 1;
        var route = _cityTransportCatalog.Get(routeIndex);
        if (route == null) return 2;
        if (alreadyRiding) return 4;
        return _playerStore.TrySpendMoney(pid, route.UseFee) ? (byte)0 : (byte)3;
    }

    // Locked so two concurrent op104 requests cannot both pay for one recorded ride.
    public byte TransportEnter(int pid, byte routeIndex)
    {
        lock (_transportEntryLock)
        {
            byte answer = TryEnterTransport(pid, routeIndex, Game.PlayerFor(pid).IsOnTransport);
            if (answer == 0 && !Game.PlayerFor(pid).TryBoardTransport(routeIndex))
                throw new InvalidOperationException("Transport entry changed during the boarding transaction.");
            return answer;
        }
    }

    // Saves the destination, tells the squad, then sends LoadMap.
    internal void MoveMap(Client client, int pid, int mapIndex, int position)
    {
        if (pid > 0)
        {
            LeaveCurrentPresence(client, pid);
            // City travel reconnects the TCP link; the saved location carries over by PID.
            var previous = _characterStore.GetCityLocation(pid);
            if (previous.Map != mapIndex) Game.PlayerFor(pid).MoveMapReturn = previous;
            Game.PlayerFor(pid).CityMap = mapIndex;
            _characterStore.SetCityLocation(pid, mapIndex, position);
            _squadManager.BroadcastSquad(pid, (_, target) => target.SendPacket(new SquadMoveMap
            {
                Pid = pid,
                MapIndex = mapIndex < 0 ? byte.MaxValue : (byte)mapIndex
            }));
        }
        ArmExpectedCityTravel(client);
        client.SendPacket(new LoadMap { Index = mapIndex, Position = position });
    }

    // Native `b` stores the signed INDEX_NONE sentinel as 0xFF even though the script API is int.
    public static int DecodeMapByte(byte value) => value == byte.MaxValue ? -1 : value;

    public (int Map, int Area) ResolveMoveMapReturn((int Map, int Area)? previous, byte characterIndex)
    {
        if (previous is { } p && p.Map >= 0 && _cityZoneCatalog.IsValidArea(p.Map, p.Area, characterIndex))
            return p;
        return (CharacterStore.TrainingCenterMap(characterIndex), 0);
    }

    // The area must exist in the player's current zone and that zone must belong to their faction.
    public bool IsReportedCityAreaValid(int map, byte area, byte characterIndex) =>
        _cityZoneCatalog.IsValidArea(map, area, characterIndex);

    public static int ResolvePage(int direction, int current, int totalPages)
    {
        if (totalPages <= 0) return 0;
        current = direction switch
        {
            0 or 4 => 0,       // PAGE_BEGIN / PAGE_SEARCH
            2 => totalPages - 1,
            5 => current - 1,
            6 => current + 1,
            _ => current       // PAGE_CUR / PAGE_REFRESH
        };
        return Math.Clamp(current, 0, totalPages - 1);
    }

    // Server command for every player (not retail). HuxleyChatMenu sends unknown "/" commands as
    // ordinary chat, and OnMMOChat hides any line starting with "/", so nobody else sees it.
    public static bool IsUnstuckCommand(string message) =>
        message.Trim() is var text && (text.Equals("/unstuck", StringComparison.OrdinalIgnoreCase)
            || text.Equals("!unstuck", StringComparison.OrdinalIgnoreCase));

    // Reloads at the spawn of the last entered city area, else the zone's main spawn, else the
    // training center. In client-hosted missions and battles it can only suggest respawning.
    internal void Unstuck(Client client, int pid)
    {
        if (pid <= 0) return;
        if (_questRoomMembers.QuestOf(client.Id) != 0 || _battleInstanceManager.IsInBattle(pid))
        {
            ChatManager.SendSystemChat(client, "/unstuck works in cities. In a mission or battle, use respawn or leave the match.",
                "/unstuck은 도시에서만 쓸 수 있습니다. 미션이나 전투 중에는 리스폰하거나 매치를 나가세요.");
            return;
        }
        long now = Environment.TickCount64;
        if (Game.PlayerFor(pid).UnstuckAt is long last && now - last < UnstuckCooldownMs)
        {
            long seconds = (UnstuckCooldownMs - (now - last) + 999) / 1000;
            ChatManager.SendSystemChat(client, $"/unstuck is available again in {seconds} s.",
                $"/unstuck은 {seconds}초 후에 다시 쓸 수 있습니다.");
            return;
        }
        Game.PlayerFor(pid).UnstuckAt = now;
        byte index = _characterStore.Get(pid)?.Index ?? 0;
        var saved = _characterStore.GetCityLocation(pid);
        int zone = Game.PlayerFor(pid).CityMap ?? saved.Map;
        int savedArea = saved.Map == zone ? saved.Area : 0;
        var (map, area) = _questManager.IsQuestLevelZone(zone) ? (CharacterStore.TrainingCenterMap(index), 0)
            : _cityZoneCatalog.IsValidArea(zone, savedArea, index) ? (zone, savedArea)
            : _cityZoneCatalog.IsValidArea(zone, 0, index) ? (zone, 0)
            : (CharacterStore.TrainingCenterMap(index), 0);
        _logger.LogInformation("[Unstuck] PID {0} reloads zone {1}, area {2}.", pid, map, area);
        MoveMap(client, pid, map, area);
    }

    internal void BroadcastPresence(Client sender, IPacketSerialize packet)
    {
        foreach (var target in _playerManager.OnlineClients)
            if (!ReferenceEquals(target, sender)
                && SharesPresence(sender, target))
                target.SendPacket(packet);
    }

    internal void BroadcastPresence(Client sender, int senderPid, IPacketSerialize packet)
    {
        if (senderPid <= 0) return;
        int instance = _questRoomMembers.InstanceOf(sender.Id);
        int cityMap = Game.PlayerFor(senderPid).CityMap ?? 0;
        foreach (var target in _playerManager.OnlineClients)
        {
            int targetPid = Game.PidOf(target);
            if (ReferenceEquals(target, sender) || targetPid <= 0 || targetPid == senderPid) continue;
            if (SharesPresence(instance, cityMap, _questRoomMembers.InstanceOf(target.Id),
                    Game.PlayerFor(targetPid).CityMap ?? 0))
                target.SendPacket(packet);
        }
    }

    public static bool SharesPresence(int instanceA, int cityMapA, int instanceB, int cityMapB) =>
        instanceA > 0 || instanceB > 0
            ? instanceA > 0 && instanceA == instanceB
            : cityMapA == cityMapB;

    internal bool SharesPresence(Client a, Client b)
    {
        int pidA = Game.PidOf(a);
        int pidB = Game.PidOf(b);
        return pidA > 0 && pidB > 0 && SharesPresence(
            _questRoomMembers.InstanceOf(a.Id), (Game.PlayerFor(pidA).CityMap ?? 0),
            _questRoomMembers.InstanceOf(b.Id), (Game.PlayerFor(pidB).CityMap ?? 0));
    }

    public static IReadOnlyList<IPacketSerialize> BuildExpiredArmorPresence(
        int pid, PlayerStore.ExpiredItem expired) =>
        expired.Inventory == InventoryType.Armor
            ? [
                new EquipArmorOther { Pid = pid, Part = expired.Cell, ItemId = 0 },
                new QuestSubItem { Pid = pid, Inventory = InventoryType.Armor, Cell = expired.Cell }
            ]
            : [];

    internal void LeaveCurrentPresence(Client client, int pid)
    {
        if (pid <= 0) return;
        // City LoadMap is absolute travel, so the new pawn has no DrivenVehicle. Clear the mount
        // here, before a same-socket Spawn or observer snapshot can publish it.
        VehicleManager.ClearMountedVehicleOnPresenceExit(pid, out _);
        if (!Game.PlayerFor(pid).TryClearSpawned()) return;
        // Disconnect and character select clear client.Player before releasing the character, so
        // the leaver's presence is taken from its PID, not from the client.
        BroadcastPresence(client, pid, new DelPlayer { Pid = pid });
    }

    internal static void ArmExpectedCityTravel(Client client) =>
        client.Session.ExpectedCityTravelAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static bool IsExpectedCityTravelDisconnect(int pid, long armedAtUnixMilliseconds,
        long nowUnixMilliseconds)
    {
        long age = nowUnixMilliseconds - armedAtUnixMilliseconds;
        return pid > 0 && armedAtUnixMilliseconds > 0 && age >= 0 && age <= 120_000;
    }
}
