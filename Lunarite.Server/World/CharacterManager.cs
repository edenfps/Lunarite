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

public sealed class CharacterManager
{
    private readonly ILogger _logger;
    private readonly CityManager _cityManager;
    private readonly ClanManager _clanManager;
    private readonly ItemManager _itemManager;
    private readonly QuestInstanceManager _questInstanceManager;

    private readonly AccountStore _accountStore;
    private readonly BattleResultStore _battleResultStore;
    private readonly CharacterDataLifecycle _characterDataLifecycle;
    private readonly CharacterStore _characterStore;
    private readonly ClanStore _clanStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly EconomyRates _economyRates;
    private readonly LicenseStore _licenseStore;
    private readonly PersonalBuffStore _personalBuffStore;
    private readonly PlayerManager _playerManager;
    private readonly PlayerStore _playerStore;
    private readonly QuestStore _questStore;
    private readonly TransmitStore _transmitStore;

    private readonly CityZoneCatalog _cityZoneCatalog;
    private readonly QuestCatalog _questCatalog;
    private readonly QuestManager _questManager;
    private readonly ServerEvents _serverEvents;

    private readonly MultiStoreTransaction _multiStoreTransaction;

    private readonly QuestRoomMembers _questRoomMembers;

    private readonly WardrobeManager _wardrobeManager;

    public CharacterManager(ILogger<CharacterManager> logger, AccountStore accountStore,
        BattleResultStore battleResultStore, CharacterDataLifecycle characterDataLifecycle,
        CharacterStore characterStore, ClanStore clanStore, DurativeItemStore durativeItemStore,
        EconomyRates economyRates, LicenseStore licenseStore, PersonalBuffStore personalBuffStore,
        PlayerManager playerManager, PlayerStore playerStore, QuestStore questStore,
        TransmitStore transmitStore, CityManager cityManager, ClanManager clanManager,
        ItemManager itemManager, QuestInstanceManager questInstanceManager, CityZoneCatalog cityZoneCatalog, QuestCatalog questCatalog, QuestManager questManager, ServerEvents serverEvents, MultiStoreTransaction multiStoreTransaction, QuestRoomMembers questRoomMembers,
        WardrobeManager wardrobeManager)
    {
        _wardrobeManager = wardrobeManager;
        _questRoomMembers = questRoomMembers;
        _logger = logger;
        _accountStore = accountStore;
        _battleResultStore = battleResultStore;
        _characterDataLifecycle = characterDataLifecycle;
        _characterStore = characterStore;
        _clanStore = clanStore;
        _durativeItemStore = durativeItemStore;
        _economyRates = economyRates;
        _licenseStore = licenseStore;
        _personalBuffStore = personalBuffStore;
        _playerManager = playerManager;
        _playerStore = playerStore;
        _questStore = questStore;
        _transmitStore = transmitStore;
        _cityManager = cityManager;
        _clanManager = clanManager;
        _itemManager = itemManager;
        _questInstanceManager = questInstanceManager;
        _cityZoneCatalog = cityZoneCatalog;
        _questCatalog = questCatalog;
        _questManager = questManager;
        _serverEvents = serverEvents;
        _multiStoreTransaction = multiStoreTransaction;
    }

    // Agent reports LOGIN_SAMEUSER while the account has a Game link or a character detached into
    // a client-hosted quest world. The Agent link itself is tracked by Agent.
    public bool HasActiveAccount(int userId)
    {
        if (userId <= 0) return false;
        if (_playerManager.Links.Any(connection => connection.Session.AccountId == userId)) return true;
        return _questRoomMembers.DetachedPids.Any(pid => _characterStore.Get(pid)?.OwnerUserId == userId);
    }

    // Entering a quest world closes the city Game socket; the character stays online to
    // social rosters until the quest session is cleaned up.
    public bool IsPlayerOnline(int pid) => pid > 0
        && (_playerManager.IsOnline(pid) || _questRoomMembers.IsDetached(pid));

    public static byte LevelClassForLevel(int level) => level >= 30 ? (byte)3
        : level >= 20 ? (byte)2 : level >= 10 ? (byte)1 : (byte)0;

    public static byte GetLevelClass(int level) => level >= 30 ? (byte)3
        : level >= 20 ? (byte)2
        : level >= 10 ? (byte)1
        : (byte)0;

    // Native OnEvent consumes `dbd` (PID, event bit, value). Exported ELevelClass maps
    // C/B/A/S to 0/1/2/3 and promotes at levels 10/20/30.
    public static IReadOnlyList<WorldEvent> BuildLevelProgressionEvents(int pid, int previousLevel,
        int currentLevel)
    {
        if (pid <= 0 || currentLevel <= previousLevel) return [];
        List<WorldEvent> events = [new() { Pid = pid, Event = 0x01, Value = currentLevel }];
        int previousClass = LevelClassForLevel(previousLevel);
        int currentClass = LevelClassForLevel(currentLevel);
        if (currentClass > previousClass)
            events.Add(new WorldEvent { Pid = pid, Event = 0x02, Value = currentClass });
        return events;
    }

    internal void SendLevelProgressionEvents(Client client, int pid, int previousLevel,
        int currentLevel)
    {
        foreach (var packet in BuildLevelProgressionEvents(pid, previousLevel, currentLevel))
        {
            client.SendPacket(packet);
            _cityManager.BroadcastPresence(client, packet);
        }
    }

    // PARAMETER_RANK, then EVENT_GROUP_UP (0x10) when a new named rank was reached, otherwise
    // EVENT_RANK_UP (0x04), with the new rank as the value (HuxleyMMOPlayerController.SetSelfEvent).
    internal void PublishRankUp(Client client, int pid, byte oldRank, byte newRank, int lpGained)
    {
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_RANK, Value = newRank });
        if (lpGained > 0)
            client.SendPacket(new Parameter { Param = Parameter.PARAMETER_LP,
                Value = (int)Math.Min(int.MaxValue, _playerStore.GetProgress(pid).LP) });
        var rankEvent = new WorldEvent { Pid = pid, Event = (byte)(lpGained > 0 ? 0x10 : 0x04), Value = newRank };
        client.SendPacket(rankEvent);
        _cityManager.BroadcastPresence(client, rankEvent);
        _logger.LogInformation("[Rank] PID {0} promoted from rank {1} to {2} (+{3} LP).", pid, oldRank, newRank, lpGained);
    }

    // HuxleyCityZoneData Zone_TutorialQuest zones whose Level is a QuestType_Tutorial quest index:
    // zone 8 / 810101 (Q-SP_Begin) for Sapiens, zone 108 / 860101 (Q-AT_Begin) for Alternatives.
    public (int Zone, int Quest)? TutorialStart(byte characterIndex)
    {
        var faction = characterIndex >= 10 ? CityZoneCatalog.Faction.Alternatives : CityZoneCatalog.Faction.Sapiens;
        foreach (var zone in _cityZoneCatalog.All.OrderBy(z => z.EntryIndex))
        {
            if (zone.ZoneType != "Zone_TutorialQuest" || zone.OwnerFaction != faction
                || !int.TryParse(zone.Level, out int questIndex)) continue;
            if (_questCatalog.Get(questIndex) is { QuestType: "QuestType_Tutorial" })
                return (zone.EntryIndex, questIndex);
        }
        return null;
    }

    public static bool ShouldStartTutorial(int? savedCityMap, bool tutorialAcquired, bool tutorialCompleted) =>
        !tutorialCompleted && (savedCityMap == null || tutorialAcquired);

    // Solo quest start that skips the QuestCheck/decision round trip and sends op42 directly.
    internal void StartPendingTutorial(Client client, int pid)
    {
        if (client.Session.PendingTutorialQuest is not int questIndex) return;
        client.Session.PendingTutorialQuest = null;
        var pending = new QuestInstanceManager.PendingQuestDecision
        {
            QuestIndex = questIndex,
            LeaderPid = pid,
            Members = [pid],
            Finalized = true
        };
        pending.Answers[pid] = 0;
        _logger.LogInformation("[Tutorial] starting tutorial {0} for PID {1}.", questIndex, pid);
        _questInstanceManager.FinalizePendingQuestDecision(pending);
    }

    public ReceivedAnotherPlayerInfo? BuildAnotherPlayerInfo(int pid)
    {
        var character = _characterStore.Get(pid);
        if (character == null) return null;
        var progress = _playerStore.GetProgress(pid);
        return new ReceivedAnotherPlayerInfo
        {
            Name = character.Name,
            // HuxleyPlayerInfoMenu switches on the exact retail TypeIndex values 1/6/11/16
            // for both camp and species art. A faction-only 0/1 value matches no valid row.
            Species = character.Index,
            Level = progress.Level,
            Style = character.Style,
            Rank = progress.Rank,
            ClanMark = _clanStore.ForPid(pid)?.Mark ?? 0,
            BattleRecord = _battleResultStore.BuildAnotherPlayerRecord(pid)
        };
    }

    public PlayerInfo BuildPlayerInfo(int pid)
    {
        var character = _characterStore.Get(pid);
        var progress = _playerStore.GetProgress(pid);
        var inventory = _playerStore.GetInventory(pid, () => _itemManager.BuildInitialInventory(pid));
        return new PlayerInfo
        {
            Pid = pid,
            Name = character?.Name ?? "Player",
            Index = (byte)(character?.Index ?? 6),
            UserStatus = UserStatusFor(character),
            Money = (int)Math.Clamp(progress.Money, int.MinValue, int.MaxValue),
            QT = (int)Math.Clamp(progress.QT, int.MinValue, int.MaxValue),
            Custom = character?.Custom ?? new Custom(),
            CharInfo = { Level = progress.Level, Style = character?.Style ?? 0, Rank = progress.Rank },
            PointInfo =
            {
                BP = (int)Math.Clamp(progress.BP, int.MinValue, int.MaxValue),
                LP = (int)Math.Clamp(progress.LP, int.MinValue, int.MaxValue)
            },
            PlayerProperty =
            {
                RespawnCount = progress.RespawnCount, InvenExpandStep = progress.InvenExpandStep,
                BankExpandStep = progress.BankExpandStep
            },
            LicenseList = _licenseStore.Effective(pid).ToList(),
            SkillSlots = _playerStore.GetSkillSlots(pid),
            Medals =
            [
                new MedalInfo { MedalID = 609001, MedalCount = progress.AMedalsDonated },
                new MedalInfo { MedalID = 609101, MedalCount = progress.BMedalsDonated }
            ],
            DurativeItems = _durativeItemStore.Active(pid),
            PersonalBuffs = _personalBuffStore.Active(pid),
            ClanName = _clanStore.ForPid(pid)?.Name ?? "",
            ClanMark = _clanStore.ForPid(pid)?.Mark ?? 0,
            ClanRank = _clanStore.Grade(pid),
            Items = inventory.Select(item => new PlayerInfo.Item
            {
                ItemID = item.ItemId,
                Count = Math.Max(1, item.Count),
                InventoryIndex = item.InventoryIndex,
                Cell = item.InventoryCell,
                State = item.State,
                LimitTime = item.LimitTime,
                TuneUpOptions = item.TuneUpOptions,
                Skill0 = item.Skill,
                Skill1 = item.Skill1,
                Durability = item.WireDurability
            }).ToList()
        };
    }

    // op10 character roster for the select screen.
    internal void SendPlayerList(Client client)
    {
        if (client.Session.AccountId is not int userId) return;
        // Finish elapsed deletions before building the roster.
        foreach (int pid in _characterStore.ExpiredDeletionPids())
        {
            // A character still in play (selected before its deletion was queued, or detached in
            // a quest) is deleted by a later sweep, once it is offline.
            if (IsPlayerOnline(pid)) continue;
            if (!_characterDataLifecycle.Delete(pid))
            {
                _characterStore.Restore(pid);
                _logger.LogWarning(
                    "[Chars] canceled elapsed deletion of PID {0}: protected external state appeared after scheduling.",
                    pid);
            }
        }

        var list = new PlayerList();
        foreach (var character in _characterStore.All(userId))
        {
            var progress = _playerStore.GetProgress(character.Pid);
            list.Characters.Add(new PlayerList.Entry
            {
                Pid = character.Pid,
                Name = character.Name,
                Index = character.Index,
                Level = (short)progress.Level,
                Style = character.Style,
                Rank = progress.Rank,
                DeleteMinutesRemaining = CharacterStore.DeleteMinutesRemaining(character),
                Custom = character.Custom,
                ArmorPreview = _itemManager.BuildRosterArmorPreview(character.Pid)
            });
        }
        client.SendPacket(list);
        _logger.LogInformation("[Chars] sent roster ({0} character(s)).", list.Characters.Count);
    }

    public static bool IsGameVerifyAuthorized(bool isPc, int claimedUid, int authenticatedUid) =>
        isPc && authenticatedUid > 0 && claimedUid == authenticatedUid;

    // Binds the connection to the character and settles offline expirations.
    internal void BindSelectedCharacter(Client client, int pid, int selectionUnixSeconds,
        out List<PlayerStore.ExpiredItem> expiredItems, out List<TransmitStore.ExpiredAttachment> expiredAttachments)
    {
        if (client.Player is { Pid: var previousPid }
            && Game.TryGetClient(previousPid, out var previousClient)
            && ReferenceEquals(previousClient, client))
        {
            _cityManager.LeaveCurrentPresence(client, previousPid);
            _playerManager.SetOffline(previousPid);
        }
        client.Player = Game.PlayerFor(pid);
        _questInstanceManager.CleanupDetachedQuestSession(pid);
        client.Session.ResetMailboxArrival();
        client.Session.ClearQuestRefresh();
        Game.PlayerFor(pid).UndoSales = null;
        List<PlayerStore.ExpiredItem> items = [];
        List<TransmitStore.ExpiredAttachment> attachments = [];
        _multiStoreTransaction.Run(() =>
        {
            items = _playerStore.PruneExpiredInventory(pid);
            attachments = _transmitStore.PruneExpiredAttachments(pid);
            // Offline expirations are removed before PlayerInfo; online ones are handled by
            // SweepExpiredItems and its op95 notice.
            _durativeItemStore.PruneExpired(pid, selectionUnixSeconds);
            _personalBuffStore.PruneExpired(pid, selectionUnixSeconds);
            _itemManager.SynchronizeQuestPickupObjectives(pid,
                items.Where(item => item.Inventory == InventoryType.Main).Select(item => item.ItemId));
        });
        expiredItems = items;
        expiredAttachments = attachments;
        _playerManager.SetOnline(pid, client);
    }

    // Saved city spot, Training Center, or Simulation Center; may also queue the tutorial.
    internal (int Map, int Area) ResolveEntryMap(Client client, int pid, bool directSimulation)
    {
        var character = _characterStore.Get(pid);
        var savedLocation = _characterStore.GetCityLocation(pid);
        // New or unfinished-tutorial characters load into the Training Center and start the
        // tutorial as a solo instance from OnSpawn.
        if (character != null && TutorialStart(character.Index) is { } tutorial
            && ShouldStartTutorial(character.CityMap, _questStore.Acquired(pid).Contains(tutorial.Quest),
                _questStore.Completed(pid).Contains(tutorial.Quest)))
        {
            if (!_questStore.Acquired(pid).Contains(tutorial.Quest))
                _questStore.Acquire(pid, tutorial.Quest);
            client.Session.PendingTutorialQuest = tutorial.Quest;
            _logger.LogInformation("[Tutorial] PID {0} will start tutorial {1} after spawning.", pid, tutorial.Quest);
        }
        // Tutorial zones are not city maps: the client browses Level ("810101") as a package
        // name and asserts.
        if (character != null && _questManager.IsQuestLevelZone(savedLocation.Map))
        {
            int camp = CharacterStore.TrainingCenterMap(character.Index);
            _characterStore.SetCityLocation(pid, camp, 0);
            _logger.LogWarning("[Tutorial] PID {0} was saved in non-city zone {1}; resuming at {2}.",
                pid, savedLocation.Map, camp);
            savedLocation = (camp, 0);
        }
        // HuxleyMMOSelectPlayerMenu sends RequestPlayerSelect(pid, true) from its Simulation button
        // and on double-click of a character with a combat style. The saved city spot is kept.
        if (directSimulation && character != null && client.Session.PendingTutorialQuest == null)
        {
            var simulationCenter = (Map: CharacterStore.SimulationCenterMap(character.Index), Area: 0);
            // op139 returns here; otherwise it would fall back to the Training Center.
            Game.PlayerFor(pid).MoveMapReturn = savedLocation;
            _logger.LogInformation("[Simulation] PID {0} entering Simulation Center map {1} from character select.",
                pid, simulationCenter.Map);
            return simulationCenter;
        }
        return savedLocation;
    }

    // Selected-character state sent before travel. Retail also sends the economy multipliers here.
    internal void SendSelectedPlayerState(Client client, int pid, int unixSeconds)
    {
        client.SendPacket(BuildPlayerInfo(pid));
        client.SendPacket(PcRoomParameter());
        SendTotalKillDeath(client, pid);
        _clanManager.AnnounceClanLogin(pid);
        client.SendPacket(new DurativeItemList { Items = _durativeItemStore.Active(pid) });
        client.SendPacket(new UpdateListOfDurativeItem { UnixSeconds = unixSeconds });
        client.SendPacket(new PersonalBuffs { Buffs = _personalBuffStore.Active(pid) });
        client.SendPacket(new UpdateListOfPersonalBuffs { UnixSeconds = unixSeconds });
        var rates = _economyRates.Current;
        client.SendPacket(new PriceRate
        {
            SapiBuy = rates.SapiBuy, SapiSell = rates.SapiSell,
            AlterBuy = rates.AlterBuy, AlterSell = rates.AlterSell,
            SapiRepair = rates.SapiRepair, AlterRepair = rates.AlterRepair,
            SapiTuneUp = rates.SapiTuneUp, AlterTuneUp = rates.AlterTuneUp
        });
    }

    // Op11 already carries all seven inventories, so only offline expiry notices follow LoadMap.
    // The last notice makes the client process them in one UI refresh.
    internal static void SendExpirationNotices(Client client, List<PlayerStore.ExpiredItem> expiredItems,
        List<TransmitStore.ExpiredAttachment> expiredAttachments)
    {
        var notices = expiredItems
            .Select(x => (x.ItemId, x.TuneUpOptions, x.Type))
            .Concat(expiredAttachments.Select(x => (x.ItemId, x.TuneUpOptions, x.Type)))
            .ToArray();
        for (int i = 0; i < notices.Length; i++)
        {
            var expired = notices[i];
            client.SendPacket(new NotifyExpirationItem
            {
                ItemId = expired.ItemId,
                TuneUpOptions = expired.TuneUpOptions,
                Type = expired.Type,
                AfterSelectPlayer = i == notices.Length - 1
            });
        }
    }

    // Starting weapons. HuxleyWeaponTable locks weapons to a CombatStyle (Enforcer=1, Avenger=2,
    // Phantom=3); each style starts with one level-1 weapon of each of its two types.
    internal static int[] StyleWeapons(byte style) => style switch
    {
        1 => [100100, 100200],   // Enforcer: Shooter (shotgun) + Standard Flinger
        2 => [100499, 100501],   // Avenger: machine gun + Fist (rocket launcher)
        3 => [100899, 100701],   // Phantom: Regulator + Catseye (sniper)
        _ => [100100, 100200],   // no style yet; replaced when one is picked
    };

    private AddPlayer BuildOtherPlayerAdd(int pid)
    {
        var character = _characterStore.Get(pid);
        var progress = _playerStore.GetProgress(pid);
        var position = (Game.PlayerFor(pid).Position ?? default);
        var clan = _clanStore.ForPid(pid);
        var inventory = _playerStore.GetInventory(pid, () => _itemManager.BuildInitialInventory(pid));
        return new AddPlayer
        {
            Pid = pid,
            Name = DisplayNameFor(character),
            Index = character?.Index ?? 1,
            Custom = character?.Custom ?? new Custom(),
            X = position.X, Y = position.Y, Z = position.Z, Yaw = position.Yaw,
            Style = character?.Style ?? 0,
            Level = progress.Level,
            VehicleIndex = (Game.PlayerFor(pid).SummonedVehicle ?? 0),
            ClanName = clan?.Name ?? "",
            UserStatus = UserStatusFor(character),
            // Native OnAddPlayer reads this as an armor preview and puts every record in InventoryType.Armor.
            // Wardrobe looks replace the pieces other players see (a hidden helmet is left out).
            EquippedItems = inventory.Where(x => x.InventoryIndex == InventoryType.Armor)
                .Select(x => _wardrobeManager.ShownItem(pid, x)).OfType<PlayerStore.GenericItem>().ToList()
        };
    }

    // OnAddPlayer only carries the compact GenericItemBase preview. The op80 full-item records
    // that follow give a newly visible peer socket skills, spray and durability.
    public IReadOnlyList<UpdateArmorOther> BuildOtherPlayerArmorSnapshot(int pid) =>
        _playerStore.GetInventory(pid, () => _itemManager.BuildInitialInventory(pid))
            .Where(item => item.InventoryIndex == InventoryType.Armor)
            .OrderBy(item => item.InventoryCell)
            .Select(item => (item.InventoryCell, Shown: _wardrobeManager.ShownItem(pid, item)))
            .Where(x => x.Shown != null)
            .Select(x => new UpdateArmorOther { Pid = pid, Part = x.InventoryCell, Item = x.Shown })
            .ToArray();

    internal void SendOtherPlayerSnapshot(Client target, int pid)
    {
        var snapshot = BuildOtherPlayerAdd(pid);
        if (snapshot.VehicleIndex != 0)
            _logger.LogInformation("[Vehicle] observer snapshot: target client {0}, PID {1}, mounted {2}, city map {3}.",
                target.Id, pid, snapshot.VehicleIndex, (Game.PlayerFor(pid).CityMap ?? 0));
        target.SendPacket(snapshot);
        foreach (var armor in BuildOtherPlayerArmorSnapshot(pid))
            target.SendPacket(armor);
        target.SendPacket(_clanManager.BuildClanHeadline(pid));
    }

    internal void SendMoneyParameter(Client client, int pid) =>
        client.SendPacket(new Parameter
        {
            Param = Parameter.PARAMETER_MONEY,
            Value = (int)Math.Clamp(_playerStore.GetProgress(pid).Money, int.MinValue, int.MaxValue)
        });

    // Native UHuxleySubsystem::IsAdmin is `m_stPlayer.nUserStatus >= 100`; HuxleyMMOGame
    // then calls SetAdmin, which grants the retail GM cheat manager for that character.
    // The name tag other players see: GM characters get "[GM] " in front. Their own client keeps the
    // plain name; name lookups accept either (CharacterStore.SameName).
    public string DisplayNameFor(CharacterStore.Character? character) =>
        character == null ? ""
        : UserStatusFor(character) >= PlayerInfo.GmUserStatus ? CharacterStore.GmNamePrefix + character.Name
        : character.Name;

    public byte UserStatusFor(CharacterStore.Character? character) =>
        character != null && GmCommands.IsGmLogin(_accountStore.LoginFor(character.OwnerUserId),
            Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS"))
            ? PlayerInfo.GmUserStatus : PlayerInfo.NormalUserStatus;

    // HuxleyPlayerWrapper.OnParameter(PARAMETER_PCROOM) sets m_stPlayer.byPCRoomValid, which the
    // client copies into mission/battle travel data for the native PC-room bonus.
    internal Parameter PcRoomParameter() => new()
        { Param = Parameter.PARAMETER_PCROOM, Value = _serverEvents.PcRoomActive ? 1 : 0 };

    // HuxleyMultiGame.OnParameter copies params 13/14 into PRI.TotalKill/TotalDeath, shown as
    // Kill/Death on the ID panel. Nothing else carries them, so they go out at selection and
    // after every PvP result.
    internal void SendTotalKillDeath(Client client, int pid)
    {
        var (kills, deaths) = _battleResultStore.TotalKillsDeaths(pid);
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_TOTALKILL, Value = kills });
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_TOTALDEATH, Value = deaths });
    }

    // Level/BP/LP/QT parameters, then EVENT_LEVEL_UP / EVENT_CLASS_UP, on the character's own
    // link only; a listen host reporting for another participant gets nothing. Offline
    // characters pick the values up at login.
    public void PublishLiveProgression(int pid, int previousLevel, PlayerStore.PlayerProgress progress)
    {
        var client = QuestManager.QuestRewardLiveRecipient(pid);
        if (client == null) return;
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_LEVEL, Value = progress.Level });
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_BP,
            Value = (int)Math.Min(int.MaxValue, progress.BP) });
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_LP,
            Value = (int)Math.Min(int.MaxValue, progress.LP) });
        client.SendPacket(new Parameter { Param = Parameter.PARAMETER_QT,
            Value = (int)Math.Min(int.MaxValue, progress.QT) });
        SendLevelProgressionEvents(client, pid, previousLevel, progress.Level);
    }

    public static IReadOnlyList<ProcLicense> BuildExpiredTemporaryLicenseResults(
        IEnumerable<DurativeItem> expired, IReadOnlyCollection<int> effectiveLicenses)
    {
        var active = effectiveLicenses.ToHashSet();
        return expired.Where(item => item.CityItemGroup == 5 && item.OptionValue > 0
                && !active.Contains(item.OptionValue))
            .Select(item => item.OptionValue).Distinct()
            .Select(license => new ProcLicense
            {
                Plus = false, Result = (byte)LicenseStore.AcquireResult.Ok, License = license
            }).ToArray();
    }
}
