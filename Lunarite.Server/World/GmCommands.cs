using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class GmCommands
{
    // City location before a GM\'s first !goto, for !back (kept across city relinks).
    private readonly ConcurrentDictionary<int, (int Map, int Area)> _gotoReturns = [];

    private readonly ILogger _logger;
    private readonly CityManager _cityManager;
    private readonly QuestManager _questManager;

    private readonly AccountStore _accountStore;
    private readonly CharacterStore _characterStore;
    private readonly PlayerManager _playerManager;

    private readonly BattleInstanceManager _battleInstanceManager;
    private readonly CharacterManager _characterManager;
    private readonly CityZoneCatalog _cityZoneCatalog;
    private readonly ServerEvents _serverEvents;
    private readonly SimulationMapOverrides _simulationMapOverrides;

    private readonly QuestRoomMembers _questRoomMembers;

    private readonly PlayerStore _playerStore;
    private readonly LicenseStore _licenseStore;
    private readonly LicenseCatalog _licenseCatalog;
    private readonly ItemCatalog _itemCatalog;
    private readonly PayItemStore _payItemStore;
    private readonly TransmitStore _transmitStore;

    // !kit gear per combat style (CharacterStore style 1 Enforcer, 2 Avenger):
    //   enforcer: the level-30 [Saint Beast] set (contamination 50 each) and the level-30
    //             Discharger, Shotgun and Flinger (47 each).
    //   avenger:  the level-30 [Crusader] set (Precious, 45 each) and the Precious level-30
    //             [Rapier P] machine gun, [Fatal Crusher] optical rifle and [Dragon Fist] rocket
    //             launcher (49 each), plus the [Dragon Eyes] plasma optical rifle (47).
    internal static readonly Dictionary<string, (byte Style, int[] Items)> KitItems = new()
    {
        ["enforcer"] = (1, [230012, 230112, 230212, 230312, 230412, 130307, 130107, 130221]),
        ["avenger"] = (2, [231009, 231109, 231209, 231309, 231409, 130421, 130607, 130523, 130616]),
    };

    public GmCommands(ILogger<GmCommands> logger, AccountStore accountStore, CharacterStore characterStore,
        PlayerManager playerManager, CityManager cityManager, QuestManager questManager, BattleInstanceManager battleInstanceManager, CharacterManager characterManager, CityZoneCatalog cityZoneCatalog, ServerEvents serverEvents, SimulationMapOverrides simulationMapOverrides, QuestRoomMembers questRoomMembers,
        PlayerStore playerStore, LicenseStore licenseStore, LicenseCatalog licenseCatalog, ItemCatalog itemCatalog,
        PayItemStore payItemStore, TransmitStore transmitStore)
    {
        _transmitStore = transmitStore;
        _payItemStore = payItemStore;
        _playerStore = playerStore;
        _licenseStore = licenseStore;
        _licenseCatalog = licenseCatalog;
        _itemCatalog = itemCatalog;
        _questRoomMembers = questRoomMembers;
        _logger = logger;
        _accountStore = accountStore;
        _characterStore = characterStore;
        _playerManager = playerManager;
        _cityManager = cityManager;
        _questManager = questManager;
        _battleInstanceManager = battleInstanceManager;
        _characterManager = characterManager;
        _cityZoneCatalog = cityZoneCatalog;
        _serverEvents = serverEvents;
        _simulationMapOverrides = simulationMapOverrides;
    }

    // GM accounts are the logins listed in HUXLEY_GM_LOGINS (comma-separated). Their chat lines that
    // start with '!' are server commands and are never broadcast:
    //   !notice <text>   native op116 MESSAGE_NOTICE banner to every online player
    //   !kick <name>     disconnect the online character with that name
    //   !ban <login>     LOGIN_DISABLED for that account from now on, and disconnect it
    //   !unban <login>
    public static bool IsGmLogin(string? login, string? gmLogins) =>
        !string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(gmLogins)
        && gmLogins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(login, StringComparer.OrdinalIgnoreCase);

    internal bool IsGm(Client client) =>
        client.Session.AccountId is int userId
        && IsGmLogin(_accountStore.LoginFor(userId), Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS"));

    internal bool RunGmCommand(Client gm, string gmName, string message)
    {
        string[] parts = message.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string command = parts[0].ToLowerInvariant();
        string argument = parts.Length > 1 ? parts[1] : "";
        switch (command)
        {
            case "!notice" when argument.Length > 0: GmNotice(gmName, argument); break;
            case "!kick" when argument.Length > 0: GmKick(gm, gmName, argument); break;
            case "!ban" or "!unban" when argument.Length > 0: GmBan(gm, gmName, argument, ban: command == "!ban"); break;
            case "!pcroom" when argument is "on" or "off": GmPcRoom(gmName, active: argument == "on"); break;
            case "!goto": GmGoto(gm, gmName, argument); break;
            case "!send": GmSend(gm, gmName, argument); break;
            case "!back": GmBack(gm, gmName); break;
            case "!simmap": GmSimulationMap(gm, gmName, argument); break;
            case "!level": GmLevel(gm, gmName, argument); break;
            case "!give": GmGive(gm, gmName, argument); break;
            case "!kit": GmKit(gm, gmName, argument); break;
            case "!style": GmStyle(gm, gmName, argument); break;
            case "!money": GmMoney(gm, gmName, argument); break;
            case "!givepaid": GmGivePaid(gm, gmName, argument); break;
            case "!mail": GmMail(gm, gmName, argument); break;
            default:
                ChatManager.SendSystemChat(gm, "GM commands: !notice <text>, !kick <name>, !ban <login>, !unban <login>, !pcroom on|off, !simmap [<entry> <map>|off], !goto <zone> [area], !back, !send <name> <zone|camp> [area], !level <1-30>, !give <item> [count], !givepaid <name|all> <item> [count], !mail <name|#pid> <item> [count], !style <enforcer|avenger|phantom> [name], !money <amount> [name], !kit [enforcer|avenger].");
                break;
        }
        return true;
    }

    // !level <1-30>: the GM's own BP becomes the first value of that level (level follows BP).
    // The client reads level from the selection data, so it shows after re-entering the character.
    private void GmLevel(Client gm, string gmName, string argument)
    {
        if (!int.TryParse(argument, out int level) || level is < 1 or > 30)
        {
            ChatManager.SendSystemChat(gm, "Usage: !level <1-30>. Re-enter the character to see it.");
            return;
        }
        int pid = Game.PidOf(gm);
        long target = level == 1 ? 0 : PlayerStore.ComputePlayerLevelupGoalBP(level - 1) + 1L;
        var progress = _playerStore.GetProgress(pid);
        _playerStore.AddReward(pid, 0, target - progress.BP, 0);
        _logger.LogInformation("[GM] {0} set PID {1} to level {2} (BP {3}).", gmName, pid, level, target);
        ChatManager.SendSystemChat(gm, $"Level {level} set. Return to character select and re-enter to see it.");
    }

    // !give <item> [count]: into the first free main-inventory cell, shown at once (op82).
    private void GmGive(Client gm, string gmName, string argument)
    {
        string[] args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int count = 1;
        if (args.Length is not (1 or 2) || !int.TryParse(args[0], out int itemId)
            || (args.Length == 2 && !int.TryParse(args[1], out count)) || count is < 1 or > 999)
        {
            ChatManager.SendSystemChat(gm, "Usage: !give <item> [count 1-999].");
            return;
        }
        if (GiveItem(gm, itemId, count))
            _logger.LogInformation("[GM] {0} gave PID {1} item {2} x{3}.", gmName, Game.PidOf(gm), itemId, count);
    }

    private bool GiveItem(Client gm, int itemId, int count)
    {
        if (_itemCatalog.Get(itemId) == null)
        {
            ChatManager.SendSystemChat(gm, $"Unknown item {itemId}.");
            return false;
        }
        int pid = Game.PidOf(gm);
        if (!_playerStore.TryAddMainItem(pid, _playerStore.CreateAcquiredItem(itemId, count), out var deposited))
        {
            ChatManager.SendSystemChat(gm, "Inventory is full.");
            return false;
        }
        ItemManager.SendInventoryItem(gm, deposited);
        return true;
    }

    // !givepaid <name|all> <item> [count]: into the paid-item storage of that character's account (or of
    // every account with a character), one storage entry per item. Any character on the account can
    // claim it from the inventory's Paid tab; it needs no free inventory cell until then.
    private void GmGivePaid(Client gm, string gmName, string argument)
    {
        string[] args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int count = 1;
        if (args.Length is not (2 or 3) || !int.TryParse(args[1], out int itemId)
            || (args.Length == 3 && !int.TryParse(args[2], out count)) || count is < 1 or > 50)
        {
            ChatManager.SendSystemChat(gm, "Usage: !givepaid <name|all> <item> [count 1-50].");
            return;
        }
        if (_itemCatalog.Get(itemId) == null)
        {
            ChatManager.SendSystemChat(gm, $"Unknown item {itemId}.");
            return;
        }
        int[] accounts;
        if (args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
            accounts = _characterStore.All().Select(c => c.OwnerUserId).Distinct().ToArray();
        else if (FindCharacter(args[0]) is { } character)
            accounts = [character.OwnerUserId];
        else
        {
            ChatManager.SendSystemChat(gm, $"No character named {args[0]}.");
            return;
        }
        var entry = new CashShopCatalog.Entry(itemId, 0, 0, "Event Gift", "", "");
        foreach (int account in accounts)
            for (int i = 0; i < count; i++)
                _payItemStore.Add(account, entry, "GM");
        _logger.LogInformation("[GM] {0} put item {1} x{2} in paid storage of {3} account(s) ({4}).",
            gmName, itemId, count, accounts.Length, args[0]);
        ChatManager.SendSystemChat(gm, $"Item {itemId} x{count} sent to the Paid storage of {accounts.Length} account(s).");
    }

    // !style <enforcer|avenger|phantom> [name]: combat style of the GM's own character, or of the
    // named one. S-class weapons and armor only equip for their own style. The client reads the
    // style from the selection data, so it shows after re-entering the character.
    private void GmStyle(Client gm, string gmName, string argument)
    {
        string[] args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        byte style = args.Length is 1 or 2 ? args[0].ToLowerInvariant() switch
        {
            "enforcer" => (byte)1, "avenger" => (byte)2, "phantom" => (byte)3, _ => (byte)0
        } : (byte)0;
        if (style == 0)
        {
            ChatManager.SendSystemChat(gm, "Usage: !style <enforcer|avenger|phantom> [name].");
            return;
        }
        var character = args.Length == 2 ? FindCharacter(args[1]) : _characterStore.Get(Game.PidOf(gm));
        if (character == null)
        {
            ChatManager.SendSystemChat(gm, $"No character named {args[1]}.");
            return;
        }
        _characterStore.SetStyle(character.Pid, style);
        _logger.LogInformation("[GM] {0} set {1} (PID {2}) to combat style {3}.", gmName, character.Name, character.Pid, args[0]);
        ChatManager.SendSystemChat(gm, $"{character.Name} is now {args[0].ToLowerInvariant()}. Return to character select and re-enter to see it.");
    }

    // !money <amount> [name]: add CR (money, PARAMETER_MONEY) to the GM's own character or the named
    // one; a negative amount takes it away. An online target sees the new balance at once.
    private void GmMoney(Client gm, string gmName, string argument)
    {
        string[] args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length is not (1 or 2) || !long.TryParse(args[0], out long amount)
            || amount is 0 or < -100_000_000 or > 100_000_000)
        {
            ChatManager.SendSystemChat(gm, "Usage: !money <amount, up to 100000000> [name].");
            return;
        }
        var character = args.Length == 2 ? FindCharacter(args[1]) : _characterStore.Get(Game.PidOf(gm));
        if (character == null)
        {
            ChatManager.SendSystemChat(gm, $"No character named {args[1]}.");
            return;
        }
        long current = _playerStore.GetProgress(character.Pid).Money;
        amount = Math.Clamp(amount, -current, int.MaxValue - current);
        var progress = _playerStore.AddReward(character.Pid, 0, 0, amount);
        if (Game.TryGetClient(character.Pid, out var target))
            _characterManager.SendMoneyParameter(target, character.Pid);
        _logger.LogInformation("[GM] {0} gave {1} (PID {2}) {3} CR; balance {4}.",
            gmName, character.Name, character.Pid, amount, progress.Money);
        ChatManager.SendSystemChat(gm, $"{character.Name} now has {progress.Money} CR.");
    }

    // !kit [enforcer|avenger]: level 30, that combat style, every weapon license, and its kit gear.
    private void GmKit(Client gm, string gmName, string argument)
    {
        string style = argument.Length == 0 ? "enforcer" : argument.ToLowerInvariant();
        if (!KitItems.TryGetValue(style, out var kit))
        {
            ChatManager.SendSystemChat(gm, "Usage: !kit [enforcer|avenger].");
            return;
        }
        int pid = Game.PidOf(gm);
        GmLevel(gm, gmName, "30");
        _characterStore.SetStyle(pid, kit.Style);
        int licenses = _licenseCatalog.All.Where(l => l.WeaponGroups.Count != 0)
            .Count(l => _licenseStore.Grant(pid, l.EntryIndex));
        int given = kit.Items.Count(id => GiveItem(gm, id, 1));
        _logger.LogInformation("[GM] {0} kit for PID {1}: {2}, {3} new weapon licenses, {4} items.",
            gmName, pid, style, licenses, given);
        ChatManager.SendSystemChat(gm, $"Kit: level 30, {style}, {licenses} weapon licenses, {given} items. Re-enter the character, then equip from the inventory.");
    }

    // A name, or #<pid> for a name the GM's client cannot type (a Korean name on an English client).
    private CharacterStore.Character? FindCharacter(string name) =>
        name.StartsWith('#') && int.TryParse(name.AsSpan(1), out int pid) ? _characterStore.Get(pid)
        : _characterStore.All().FirstOrDefault(c => CharacterStore.SameName(c.Name, name));

    // !mail <name|#pid> <item> [count 1-999]: the item arrives in that character's mailbox as a normal
    // mail from the GM (online or not), with the usual arrival marker if the character is online.
    private void GmMail(Client gm, string gmName, string argument)
    {
        string[] args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int count = 1;
        if (args.Length is not (2 or 3) || !int.TryParse(args[1], out int itemId)
            || (args.Length == 3 && !int.TryParse(args[2], out count)) || count is < 1 or > 999)
        {
            ChatManager.SendSystemChat(gm, "Usage: !mail <name|#pid> <item> [count 1-999].");
            return;
        }
        if (_itemCatalog.Get(itemId) == null)
        {
            ChatManager.SendSystemChat(gm, $"Unknown item {itemId}.");
            return;
        }
        if (FindCharacter(args[0]) is not { } recipient)
        {
            ChatManager.SendSystemChat(gm, $"No character {args[0]}.");
            return;
        }
        _transmitStore.Add(recipient.Pid, Game.PidOf(gm), 1, gmName, "Gift", "A gift from the Lunarite team.",
            attachments: [_playerStore.CreateAcquiredItem(itemId, count)]);
        TransmitManager.NotifyTransmitArrival(recipient.Pid, 1); // TRANSMITTYPE_NORMAL
        _logger.LogInformation("[GM] {0} mailed item {1} x{2} to PID {3}.", gmName, itemId, count, recipient.Pid);
        ChatManager.SendSystemChat(gm, $"Mailed item {itemId} x{count} to PID {recipient.Pid}.");
    }

    // !notice <text>: native op116 MESSAGE_NOTICE banner to every online player.
    private void GmNotice(string gmName, string text)
    {
        var notice = new Message { Kind = ServerMessageKind.Notice, Text = text, LifeSeconds = 10 };
        foreach (var target in _playerManager.OnlineClients) target.SendPacket(notice);
        _logger.LogInformation("[GM] {0} notice: {1}", gmName, text);
    }

    private void GmKick(Client gm, string gmName, string name)
    {
        var character = FindCharacter(name);
        if (character == null || !Game.TryGetClient(character.Pid, out var target))
        {
            ChatManager.SendSystemChat(gm, $"No online character named {name}.");
            return;
        }
        ChatManager.SendSystemChat(target, "You were disconnected by a GM.", "GM에 의해 연결이 끊겼습니다.");
        target.Kick($"GM {gmName} kick");
        ChatManager.SendSystemChat(gm, $"Kicked {character.Name}.");
        _logger.LogInformation("[GM] {0} kicked {1} (PID {2}).", gmName, character.Name, character.Pid);
    }

    // !ban / !unban <login>: LOGIN_DISABLED for that account from now on; a ban also disconnects it.
    private void GmBan(Client gm, string gmName, string login, bool ban)
    {
        int userId = _accountStore.SetBanned(login, ban);
        if (userId == 0)
        {
            ChatManager.SendSystemChat(gm, $"No account named {login}.");
            return;
        }
        if (ban)
            foreach (var online in _playerManager.Links.ToArray())
                if (online.Session.AccountId == userId && _playerManager.OnlineClients.Contains(online))
                {
                    ChatManager.SendSystemChat(online, "This account has been banned.", "이 계정은 이용이 정지되었습니다.");
                    online.Kick($"GM {gmName} ban");
                }
        ChatManager.SendSystemChat(gm, $"{(ban ? "Banned" : "Unbanned")} account {login}.");
        _logger.LogInformation("[GM] {0} {1} account {2} (user {3}).", gmName, ban ? "banned" : "unbanned", login, userId);
    }

    private void GmPcRoom(string gmName, bool active)
    {
        _serverEvents.SetPcRoom(active);
        var parameter = _characterManager.PcRoomParameter();
        var announcement = new Message
        {
            Kind = ServerMessageKind.Notice, LifeSeconds = 10,
            Text = active ? "PC-room event started: +50% BP and PCr, PC-room weapons unlocked."
                : "PC-room event ended."
        };
        foreach (var target in _playerManager.OnlineClients)
        {
            target.SendPacket(parameter);
            target.SendPacket(announcement);
        }
        _logger.LogInformation("[GM] {0} turned the PC-room event {1}.", gmName, active ? "on" : "off");
    }

    // !goto <zone> [area]: city zone travel, including the old towns
    //: 98 City-SP-motorbike, 99 City-SP-Camptown
    // (Sapiens); 107 City-Eska_HQ_Lobby, 109 City-AT-Camptown (Alternatives).
    private void GmGoto(Client gm, string gmName, string argument)
    {
        string[] args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int gmPid = Game.PidOf(gm);
        byte index = _characterStore.Get(gmPid)?.Index ?? 0;
        if (args.Length is 1 or 2 && int.TryParse(args[0], out int zone)
            && (args.Length == 1 ? 0 : int.TryParse(args[1], out int a) ? a : -1) is int area and >= 0
            && _cityZoneCatalog.IsValidArea(zone, area, index) && !_questManager.IsQuestLevelZone(zone))
        {
            _logger.LogInformation("[GM] {0} travels to zone {1}, area {2}.", gmName, zone, area);
            // Keep the first origin of a !goto chain, so !back returns to where it began.
            _gotoReturns.TryAdd(gmPid, _characterStore.GetCityLocation(gmPid));
            _cityManager.MoveMap(gm, gmPid, zone, area);
        }
        else ChatManager.SendSystemChat(gm,
            "Usage: !goto <zone> [area]. Old towns: 98 SP-Motorbike, 99 SP-Camptown (Sapiens); 107 AT-HQ-Lobby, 109 AT-Camptown (Alternatives).");
    }

    // !send <name> <zone|camp> [area]: move an online player to a city zone of their own faction.
    // "camp" is that character's Training Center (Eska has no train back).
    private void GmSend(Client gm, string gmName, string argument)
    {
        string[] args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var character = args.Length is 2 or 3 ? FindCharacter(args[0]) : null;
        if (character == null)
        {
            ChatManager.SendSystemChat(gm, "Usage: !send <name> <zone|camp> [area].");
            return;
        }
        if (!Game.TryGetClient(character.Pid, out var target))
        {
            ChatManager.SendSystemChat(gm, $"No online character named {args[0]}.");
            return;
        }
        if (_questRoomMembers.QuestOf(target.Id) != 0 || _battleInstanceManager.IsInBattle(character.Pid))
        {
            ChatManager.SendSystemChat(gm, $"{character.Name} is in a mission or battle; try again once they are in a city.");
            return;
        }
        int zone = args[1].Equals("camp", StringComparison.OrdinalIgnoreCase)
            ? CharacterStore.TrainingCenterMap(character.Index)
            : int.TryParse(args[1], out int z) ? z : -1;
        int area = args.Length == 2 ? 0 : int.TryParse(args[2], out int a) ? a : -1;
        if (area < 0 || !_cityZoneCatalog.IsValidArea(zone, area, character.Index) || _questManager.IsQuestLevelZone(zone))
        {
            ChatManager.SendSystemChat(gm, $"Zone {args[1]} area {area} is not a city area {character.Name} can enter.");
            return;
        }
        _logger.LogInformation("[GM] {0} sends {1} (PID {2}) to zone {3}, area {4}.",
            gmName, character.Name, character.Pid, zone, area);
        ChatManager.SendSystemChat(target, "A GM is moving you.", "GM이 캐릭터를 이동시키고 있습니다.");
        _cityManager.MoveMap(target, character.Pid, zone, area);
        ChatManager.SendSystemChat(gm, $"Sent {character.Name} to zone {zone}, area {area}.");
    }

    private void GmBack(Client gm, string gmName)
    {
        int gmPid = Game.PidOf(gm);
        byte index = _characterStore.Get(gmPid)?.Index ?? 0;
        if (_gotoReturns.TryRemove(gmPid, out var origin)
            && _cityZoneCatalog.IsValidArea(origin.Map, origin.Area, index))
        {
            _logger.LogInformation("[GM] {0} returns to zone {1}, area {2}.", gmName, origin.Map, origin.Area);
            _cityManager.MoveMap(gm, gmPid, origin.Map, origin.Area);
        }
        else ChatManager.SendSystemChat(gm, "No !goto origin to return to.");
    }

    // !simmap                     -> list overrides and available maps
    // !simmap <entry> <level>     -> rooms on <entry> host <level>
    // !simmap <entry> off         -> restore the cooked map
    private void GmSimulationMap(Client gm, string gmName, string argument)
    {
        string[] args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 2 && int.TryParse(args[0], out int entry))
        {
            if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                ChatManager.SendSystemChat(gm, _simulationMapOverrides.Clear(entry)
                    ? $"Entry {entry} hosts its own map again." : $"Entry {entry} had no override.");
            else if (_simulationMapOverrides.Set(entry, args[1]) is { } level)
            {
                ChatManager.SendSystemChat(gm, $"Simulation rooms on entry {entry} now host {level}.");
                _logger.LogInformation("[GM] {0} set Simulation entry {1} to {2}.", gmName, entry, level);
            }
            else ChatManager.SendSystemChat(gm, $"Unknown entry {entry} or unavailable map {args[1]}.");
            return;
        }
        string current = string.Join(", ", _simulationMapOverrides.All.Select(x => $"{x.Key}={x.Value}"));
        ChatManager.SendSystemChat(gm, $"Overrides: {(current.Length == 0 ? "none" : current)}.");
        ChatManager.SendSystemChat(gm, "Maps: " + string.Join(", ", SimulationMapOverrides.AvailableLevels));
    }
}
