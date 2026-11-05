using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class BattleManager
{
    private readonly ILogger _logger;
    private readonly ItemManager _itemManager;

    private readonly CharacterStore _characterStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly LicenseStore _licenseStore;
    private readonly PersonalBuffStore _personalBuffStore;
    private readonly PlayerManager _playerManager;
    private readonly PlayerStore _playerStore;
    private readonly QuestEntryReservationStore _questEntryReservationStore;

    private readonly ConcurrentDictionary<int, byte> _battleReservations = [];

    private readonly BattleFront _battleFront;
    private readonly BattleInstanceManager _battleInstanceManager;
    private readonly BattleZoneCatalog _battleZoneCatalog;

    private readonly System.Threading.Timer _battleFrontTimer;

    private readonly BattleServerManager _battleServerManager;

    public const int BattleMedalItem = 609001; // HuxleyMultiGame.AMedalItemIndex

    public BattleManager(ILogger<BattleManager> logger, CharacterStore characterStore,
        DurativeItemStore durativeItemStore, LicenseStore licenseStore, PersonalBuffStore personalBuffStore,
        PlayerManager playerManager, PlayerStore playerStore,
        QuestEntryReservationStore questEntryReservationStore, ItemManager itemManager, BattleFront battleFront, BattleInstanceManager battleInstanceManager, BattleZoneCatalog battleZoneCatalog, BattleServerManager battleServerManager)
    {
        _logger = logger;
        _characterStore = characterStore;
        _durativeItemStore = durativeItemStore;
        _licenseStore = licenseStore;
        _personalBuffStore = personalBuffStore;
        _playerManager = playerManager;
        _playerStore = playerStore;
        _questEntryReservationStore = questEntryReservationStore;
        _itemManager = itemManager;
        _battleFront = battleFront;
        _battleInstanceManager = battleInstanceManager;
        _battleZoneCatalog = battleZoneCatalog;
        _battleServerManager = battleServerManager;
        _battleFrontTimer = new(_ => SendBattleFrontInvites(), null,
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    // The battlefield map (HuxleyBattleFieldScene: SendBattleIntrusion(BattleZoneData.AxisID)) and
    // the battle invite (HuxleyBattleInviteScene: SendBattleEnter(Axis)) pass a war-front axis;
    // the battle-zone menu passes a BattleServerID. Axis ids (0..2) never match a zone's server
    // id, so an axis id resolves to that axis's open battleground.
    public int ResolveBattleRequest(int requested)
    {
        if (_battleZoneCatalog.GetByServerId(requested) != null || !_battleFront.Axes.Contains(requested))
            return requested;
        int map = _battleFront.BattlegroundOf(requested);
        return _battleZoneCatalog.All.FirstOrDefault(z => z.EntryIndex == map)?.BattleServerId ?? requested;
    }

    internal void SendBattleAdmission(Client client, byte requestedId)
    {
        int pid = Game.PidOf(client);
        byte serverId = (byte)ResolveBattleRequest(requestedId);
        _logger.LogInformation("[BattleFront] PID {0} requests battle {1} -> server {2}.", pid, requestedId, serverId);
        if (_battleZoneCatalog.GetByServerId(serverId) is { OnFront: true } frontZone
            && !_battleFront.CanEnter(frontZone.EntryIndex))
        {
            // BattleMsg[3] "does not have ongoing combat yet" for a resting battleground,
            // BattleMsg[23] "Non-combat area" for a map the front is not on.
            bool resting = _battleFront.BattlegroundOf(frontZone.Axis) == frontZone.EntryIndex;
            _logger.LogWarning("[BattleFront] PID {0} refused: map {1} is {2} of axis {3}.", pid, frontZone.EntryIndex,
                resting ? "the resting battleground" : "not the open battleground", frontZone.Axis);
            client.SendPacket(new Battle { Answer = resting ? BattleAnswer.NoCombatYet : BattleAnswer.NonCombatArea });
            return;
        }
        int registerFee = _battleZoneCatalog.GetByServerId(serverId)?.RegisterFee ?? 0;
        if (registerFee > 0 && !_battleInstanceManager.IsMemberOf(pid, serverId)
            && _playerStore.GetInventory(pid, () => [])
                .Where(x => x.InventoryIndex == InventoryType.Main && x.ItemId == BattleMedalItem)
                .Sum(x => x.Count) < registerFee)
        {
            client.SendPacket(new Battle { Answer = BattleAnswer.NotEnoughBattleMedals });
            return;
        }
        var entry = _battleInstanceManager.TryEnter(pid, serverId, out var admission);
        if (entry == BattleInstanceManager.EnterResult.AlreadyEntering)
        {
            // A repeated click while the first request launches; that request answers.
            _logger.LogInformation("[BattleFront] PID {0}: entry already in progress, request {1} dropped.", pid, serverId);
            return;
        }
        if (entry == BattleInstanceManager.EnterResult.Committed)
        {
            client.SendPacket(new Battle { Answer = BattleAnswer.AlreadyParticipating });
            return;
        }
        if (entry != BattleInstanceManager.EnterResult.Admitted || admission == null)
        {
            client.SendPacket(new Battle { Answer = BattleAnswer.LoadingRoomFailed });
            return;
        }
        _logger.LogInformation("[BattleFront] PID {0} {1} server {2} ({3}); registering with its dedicated server.",
            pid, admission.Rejoin ? "rejoins" : "admitted to", serverId, admission.LevelName);
        if (!_battleServerManager.TryRegisterPlayer(serverId, BuildPermissionRegister(pid, admission.Key)))
        {
            _logger.LogWarning("[BattleFront] PID {0}: dedicated server {1} did not accept the player registration.",
                pid, serverId);
            if (!admission.Rejoin) _battleInstanceManager.Leave(pid);
            client.SendPacket(new Battle { Answer = BattleAnswer.LoadingRoomFailed });
            return;
        }
        // The fee is taken only once the dedicated server accepted the player, and once per stay.
        if (!admission.Rejoin && registerFee > 0 && !TryPayRegisterFee(client, pid, registerFee, serverId))
        {
            _battleInstanceManager.Leave(pid);
            client.SendPacket(new Battle { Answer = BattleAnswer.NotEnoughBattleMedals });
            return;
        }
        // Battle instances run on this machine and advertise BATTLE_HOST_IP (127.0.0.1 by default);
        // a remote player gets the address it reached the game server on instead, as co-op hosts do.
        client.SendPacket(new Battle
        {
            Answer = BattleAnswer.Ok, ServerPort = admission.Port, Key = admission.Key,
            ServerIp = unchecked((int)P2PManager.HostIpFor(unchecked((uint)admission.ServerIp), client))
        });
    }

    private bool TryPayRegisterFee(Client client, int pid, int registerFee, byte serverId)
    {
        if (!_playerStore.TryConsumeQuestEntryItems(pid, [(BattleMedalItem, registerFee)], out var feeCells))
            return false;
        var feeInventory = _playerStore.GetInventory(pid, () => []);
        foreach (byte cell in feeCells.Distinct())
        {
            client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = cell });
            if (feeInventory.FirstOrDefault(x => x.InventoryIndex == InventoryType.Main && x.InventoryCell == cell)
                is { } remaining)
                ItemManager.SendInventoryItem(client, remaining);
        }
        _logger.LogInformation("[BattleFront] PID {0} paid a {1}-medal register fee for server {2}.", pid, registerFee, serverId);
        return true;
    }

    private BattleBackendPermissionRegister BuildPermissionRegister(int pid, byte[] key)
    {
        var progress = _playerStore.GetProgress(pid);
        var character = _characterStore.Get(pid);
        return new BattleBackendPermissionRegister
        {
            Key = key,
            Pid = pid,
            PlayerName = character?.Name ?? "",
            Character = (byte)(character?.Index ?? 6),
            Money = (int)Math.Clamp(progress.Money, int.MinValue, int.MaxValue),
            Appearance = character?.Custom ?? new Custom(),
            Level = progress.Level,
            Style = character?.Style ?? 0,
            Rank = progress.Rank,
            BP = (int)Math.Clamp(progress.BP, int.MinValue, int.MaxValue),
            LP = (int)Math.Clamp(progress.LP, int.MinValue, int.MaxValue),
            RespawnCount = progress.RespawnCount,
            InvenExpandStep = progress.InvenExpandStep,
            BankExpandStep = progress.BankExpandStep,
            DurativeItems = _durativeItemStore.Active(pid),
            PersonalBuffs = _personalBuffStore.Active(pid),
            Licenses = _licenseStore.Effective(pid).ToList(),
            SkillSlots = _playerStore.GetSkillSlots(pid),
            Items = _itemManager.StartingLoadoutItems(pid)
        };
    }

    // Native op34 OnBattleOccupation(server, from, to) drives HuxleyPCMMOHUD's defense/deprived/
    // occupation/defeated message for every online player.
    public void PublishBattleFrontOutcome(BattleFront.Outcome outcome)
    {
        var packet = new BattleOccupation
        {
            Server = (byte)Math.Clamp(outcome.Map, 0, byte.MaxValue),
            FromSpecies = outcome.FromSpecies,
            ToSpecies = outcome.ToSpecies
        };
        foreach (var client in _playerManager.OnlineClients) client.SendPacket(packet);
        _logger.LogInformation("[BattleFront] map {0}: species {1} -> {2}; next battleground {3}.",
            outcome.Map, outcome.FromSpecies, outcome.ToSpecies, outcome.NextBattleground);
    }

    // While an axis's battleground is out of recess, players who reserved that axis get native op30
    // OnBattleInvite(axis, area = battlefield map), which opens UIBattleField.BattleInviteScene.
    // An invite consumes the reservation, so each sweep reaches only players who reserved since the
    // last one, including reservations made while the axis was already open or after a result that
    // left the battleground on the same map.
    internal int SendBattleFrontInvites()
    {
        int sent = 0;
        foreach (int axis in _battleFront.Axes) sent += InviteReservations(axis);
        return sent;
    }

    internal int InviteReservations(int axis)
    {
        int ground = _battleFront.BattlegroundOf(axis);
        if (ground <= 0 || _battleFront.RecessMinutesLeft(ground) > 0) return 0;
        int sent = 0;
        // The client's OnBattleInvite clears its own reservation; drop it here too so the axis
        // counts and reserve status stop showing an invited player as still queued.
        foreach (var reservation in _battleReservations.Where(x => x.Value == axis).ToList())
            if (Game.TryGetClient(reservation.Key, out var client)
                && _battleReservations.TryRemove(new KeyValuePair<int, byte>(reservation.Key, reservation.Value)))
            {
                client.SendPacket(new BattleInvite { Axis = (byte)axis, Area = (byte)Math.Clamp(ground, 0, byte.MaxValue) });
                sent++;
            }
        return sent;
    }

    // The axis panel shows the current war-front battleground, whether a battle is being fought
    // there, and the wait until its recess ends. Native OnBattleAxisInfos (0x1112E0B0) splits that
    // wait into v/60 hours and v%60 minutes, so it is sent in minutes.
    internal BattleAxisEntry BuildBattleAxisEntry(byte axis, int pid)
    {
        int ground = _battleFront.BattlegroundOf(axis);
        var groundZone = _battleZoneCatalog.Get(ground);
        bool running = groundZone != null && _battleInstanceManager.IsRunning(groundZone.BattleServerId);
        // EReserveStatus, copied as-is by native OnBattleAxisInfos: 0 reservable, 1 reserved, 2 full.
        byte reserved = TryGetReservation(pid, out byte reservedAxis) && reservedAxis == axis ? (byte)1 : (byte)0;
        var counts = BattleReservationCounts(axis);
        return new BattleAxisEntry(axis, (byte)Math.Clamp(ground, 0, byte.MaxValue),
            running ? (byte)1 : (byte)0, counts.Sapi, counts.Alter,
            ground > 0 ? _battleFront.RecessMinutesLeft(ground) : 0, reserved);
    }

    // _BattleFieldInfo_: status (EBattleFieldStatus), SapiOccupy, attacker flags, battle time
    // left and next-battle wait in minutes, reserved player counts.
    internal BattleFieldEntry BuildBattleFieldEntry(byte axis, byte map)
    {
        var counts = BattleReservationCounts(axis);
        var zone = _battleZoneCatalog.Get(map);
        bool running = zone != null && _battleInstanceManager.IsRunning(zone.BattleServerId);
        var status = _battleFront.StatusOf(map, running);
        byte occupant = _battleFront.OccupantOf(map);
        bool contested = status != BattleFront.FieldStatus.Safe;
        byte battleMinutes = running && zone != null
            ? (byte)Math.Clamp((_battleServerManager.RemainingSecondsFor(zone.BattleServerId) + 59) / 60, 0, byte.MaxValue) : (byte)0;
        return new BattleFieldEntry(map, (byte)status, occupant == 1 ? (byte)1 : (byte)0,
            contested && occupant == 2 ? (byte)1 : (byte)0,   // Sapiens attack an Alternative map
            contested && occupant == 1 ? (byte)1 : (byte)0,   // Alternatives attack a Sapiens map
            battleMinutes, (byte)Math.Clamp(_battleFront.RecessMinutesLeft(map), 0, byte.MaxValue),
            counts.Sapi, counts.Alter);
    }

    internal (byte Sapi, byte Alter) BattleReservationCounts(byte axis)
    {
        int sapi = 0, alter = 0;
        foreach (var reservation in _battleReservations)
        {
            if (reservation.Value != axis || !_playerManager.IsOnline(reservation.Key)) continue;
            if ((_characterStore.Get(reservation.Key)?.Index ?? 1) >= 11) alter++;
            else sapi++;
        }
        return ((byte)Math.Min(byte.MaxValue, sapi), (byte)Math.Min(byte.MaxValue, alter));
    }

    // Battles wear gear like missions do: apply the result's per-slot durability with the same
    // rules as native quest results (never repair, ignore an empty weapon slot's zero), then
    // refresh the changed slots on the live MMO link.
    public static IReadOnlyList<(byte Inven, byte Cell, byte Durability)> BattleDurabilityRecords(byte[] block)
    {
        var records = new List<(byte, byte, byte)>();
        for (int offset = 0; offset + 2 < block.Length; offset += 3)
            records.Add((block[offset], block[offset + 1], block[offset + 2]));
        return records;
    }

    public void ApplyBattleDurability(BattleResultRequest report)
    {
        var records = BattleDurabilityRecords(report.Durability);
        if (_playerStore.UpdateNativeQuestDurability(report.Pid, records) == 0) return;
        var client = QuestManager.QuestRewardLiveRecipient(report.Pid);
        if (client == null) return;
        var inventory = _playerStore.GetInventory(report.Pid, () => []);
        foreach (var (inven, cell, _) in records)
            if (inventory.FirstOrDefault(x => x.InventoryIndex == inven && x.InventoryCell == cell) is { } item)
                ItemManager.SendInventoryItem(client, item);
    }

    internal bool TryGetReservation(int pid, out byte axis) => _battleReservations.TryGetValue(pid, out axis);

    // BattleMsg[20] "You can't double-book": a reservation on another axis stays until cancelled.
    internal byte Reserve(int pid, byte axis)
    {
        byte current = _battleReservations.GetOrAdd(pid, axis);
        return current == axis ? BattleAnswer.Ok : BattleAnswer.DoubleBooking;
    }

    internal bool CancelReservation(int pid) => _battleReservations.TryRemove(pid, out _);
}
