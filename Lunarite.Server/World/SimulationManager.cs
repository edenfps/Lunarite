using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed partial class SimulationManager
{
    private readonly ILogger _logger;
    private readonly ItemManager _itemManager;
    private readonly P2PManager _p2pManager;

    private readonly CharacterStore _characterStore;
    private readonly ClanStore _clanStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly LicenseStore _licenseStore;
    private readonly PersonalBuffStore _personalBuffStore;
    private readonly PlayerStore _playerStore;

    private int _nextSimulationRoomId;

    internal sealed class SCPlayerInfo
    {
        public int Pid;
        public byte Species;
        public byte Squad;
        public byte State;
        public bool Ready;
        public int Address;
        public ushort Port;
        public int PingGrade = 4;
    }

    internal sealed class SCRoomInfo
    {
        public byte Id;
        public int OwnerPid;
        public string Name = "";
        public string Password = "";
        public int Map;
        public byte GameType;
        public byte MaxPlayers;
        public byte GoalTime;
        public ushort GoalScore;
        public byte IsPublic;
        public byte LimitClass;
        public byte State;
        public Dictionary<int, SCPlayerInfo> Members = [];
    }

    internal sealed class SimulationTransport
    {
        public byte RoomId;
        public int OwnerPid;
        public bool Started;
        public DateTime? StartedAtUtc;
        // The op155 TimeLimit, taken when the match starts; the room settings can change after.
        public int TimeLimitSeconds;
        public List<int> Members = [];
        public HashSet<int> Pending = [];
        public HashSet<int> Confirmed = [];
        public byte[] Key = new byte[16];
        public int HostIp;
        public PeerAddrList? PeerEndpoints;
        public ushort TeamScore0;
        public ushort TeamScore1;
        public ushort RemainingTime;
        public byte BackupType;
        public byte[] BattleGameData = [];
        // Retail PlayerReplicationInfo.StartTime: when each member's UE3 login completed (op109),
        // in seconds after MatchInProgress (0 during warmup). The first admission counts, so a
        // member re-admitted by a new host after host loss keeps its original time.
        public Dictionary<int, int> JoinedAtSeconds = [];
    }

    // A match its listen host ended (op115, or an abort). Native SendBattleResult sends op115 and
    // then one op114 per player on the same link, so the host's results are accepted for a short
    // while afterwards, once per member. Seconds is the play time from MatchInProgress to the end.
    internal sealed class FinishedSimulationMatch
    {
        public DateTime FinishedAtUtc;
        public ushort GoalScore;
        public int TimeLimitSeconds;
        public int Seconds;
        public Dictionary<int, int> JoinedAtSeconds = [];
        public HashSet<int> Unsettled = [];
    }

    internal static readonly TimeSpan SimulationResultWindow = TimeSpan.FromMinutes(2);

    // The server measures play time from op141 to op115; the host's game clock can run a little
    // ahead of what reaches the server (op141 latency, timer granularity). The slack is a share
    // of the measured time, so a match of a second or two gains nothing from it.
    internal static int SimulationSlackedSeconds(int seconds) => Math.Max(0, seconds) * 21 / 20;

    // HuxleyBattleGame.GetPlayerPlayTime treats 60 seconds as the threshold of presence: a player
    // who logged in within the first 60 seconds counts as present from the start, one with 60
    // seconds or less left only gets the minimum weight. A whole match that short (by the
    // server's own clock, op141 to op115) is no match at all and pays nothing (no BP, QT,
    // Skirmish items or quest progress), so alt accounts cannot cycle near-zero matches.
    internal const int SimulationMinRewardSeconds = 60;

    // Above that minimum, BP follows retail: a score-goal match that legitimately ends early pays
    // members present from the start in full (BattleMaxBPLimit), so a host cycling minimal
    // matches can earn up to one full BP ceiling per minute; accepted to pay honest score-goal
    // rooms as retail does. QT stays bound by the measured time (durability actually spent).

    // HuxleyMMOGame.ini HuxleySimulationCenterRule.ActiveGameTypeGoals: the goal scores and time
    // limits (minutes) the room menu offers per game type (0 = no score goal).
    private static readonly Dictionary<byte, (ushort[] Scores, byte[] Times)> SimulationGoals = new()
    {
        [1] = ([0, 80, 100, 120, 150], [8, 10, 12, 15]),
        [2] = ([0, 500, 600, 800, 1000, 1200], [12, 14, 16, 18]),
        [4] = ([0, 2, 3, 5], [10, 12, 15]),
        [5] = ([0, 15, 20, 30, 40], [8, 10, 12, 15]),
        [8] = ([0, 80, 100, 120, 150], [8, 10, 12, 15]),
        [9] = ([0, 15, 20, 30, 40], [8, 10, 12, 15]),
    };

    public static bool IsSimulationGoal(byte gameType, ushort goalScore, byte goalTime) =>
        SimulationGoals.TryGetValue(gameType, out var goals)
        && goals.Scores.Contains(goalScore) && goals.Times.Contains(goalTime);

    // HuxleyMultiGameReplicationInfo.BattleMaxBPLimit[0..3] in HuxleyMMOGame.ini: the most BP a
    // Simulation match pays at level class C/B/A/S. HuxleyPCScoreBoard scales it by
    // HuxleyBattleGame.GetBonusPointRatio (1 + PCROOM_BONUS 0.5 in a PC room; EVENT_BONUS is 0).
    private static readonly int[] SimulationBattleMaxBP = [1000, 2000, 4000, 5000];

    // HuxleyBattleGame.AdjustDefaultReward scales BP by GetPlayerPlayTime (HuxleyBattleGame.uc
    // 2187-2211): a member who joined (StartTime) within the first 60 seconds weighs 1.0 however
    // early the match ends; a later joiner weighs the share of the time limit left when they
    // joined, 0.1 when that is 60 seconds or less. seconds is the measured match length, which
    // must reach SimulationMinRewardSeconds before anything is paid.
    public static double SimulationPlayTimeWeight(int seconds, int timeLimitSeconds, int joinedAtSeconds = 0)
    {
        if (seconds < SimulationMinRewardSeconds) return 0;
        if (timeLimitSeconds <= 0 || joinedAtSeconds <= SimulationMinRewardSeconds) return 1.0;
        int playTime = timeLimitSeconds - joinedAtSeconds;
        if (playTime <= SimulationMinRewardSeconds) return 0.1;
        return (double)playTime / timeLimitSeconds;
    }

    public static int SimulationBPLimit(int level, bool pcRoom, int seconds, int timeLimitSeconds,
        int joinedAtSeconds = 0) =>
        (int)(SimulationBattleMaxBP[CharacterManager.GetLevelClass(level)] * (pcRoom ? 1.5 : 1.0)
            * SimulationPlayTimeWeight(seconds, timeLimitSeconds, joinedAtSeconds));

    // HuxleyBattleGame.ComputePlayerBattleResult pays QT only for durability spent: each of up to
    // 4 weapons loses play seconds x DurabilityDecreaseRate (at most 2.4 in the weapon table) x
    // ItemDurabilityParameter[0] 0.01, each of 5 armor parts seconds x 0.01, times
    // SimulBattleAquiredQTParam (at most 91), SimulBattleQTParam (at most 1.15) and the rank param
    // (at most 1.2); then the PC-room bonus (+50%) and the listen host's 3.5%.
    // ComputeWeaponDurability/ComputeArmorDurability truncate per item, so a short match pays 0.
    public static int SimulationQTLimit(int seconds, bool pcRoom)
    {
        if (seconds < SimulationMinRewardSeconds) return 0;
        double durability = SimulationSlackedSeconds(seconds) * 0.01 * (4 * 2.4 + 5);
        return (int)Math.Ceiling(durability * 91 * 1.15 * 1.2 * (pcRoom ? 1.5 : 1.0) * 1.035);
    }

    private readonly object _simulationLock = new();
    private readonly Dictionary<byte, SCRoomInfo> _simulationRooms = [];
    private readonly Dictionary<int, byte> _simulationPidRoom = [];
    private readonly HashSet<int> _simulationChannelPids = [];
    private readonly Dictionary<int, SimulationTransport> _simulationTransports = [];
    private readonly Dictionary<int, ushort> _simulationClientPorts = [];
    private readonly Dictionary<int, FinishedSimulationMatch> _finishedSimulationMatches = [];

    private readonly BattleZoneCatalog _battleZoneCatalog;
    private readonly SimulationMapOverrides _simulationMapOverrides;

    private readonly QuestInstanceManager _questInstanceManager;

    private readonly QuestRoomMembers _questRoomMembers;

    public SimulationManager(ILogger<SimulationManager> logger, CharacterStore characterStore,
        ClanStore clanStore, DurativeItemStore durativeItemStore, LicenseStore licenseStore,
        PersonalBuffStore personalBuffStore, PlayerStore playerStore, ItemManager itemManager,
        P2PManager p2pManager, BattleZoneCatalog battleZoneCatalog, SimulationMapOverrides simulationMapOverrides, QuestInstanceManager questInstanceManager, QuestRoomMembers questRoomMembers)
    {
        _questRoomMembers = questRoomMembers;
        _questInstanceManager = questInstanceManager;
        _logger = logger;
        _characterStore = characterStore;
        _clanStore = clanStore;
        _durativeItemStore = durativeItemStore;
        _licenseStore = licenseStore;
        _personalBuffStore = personalBuffStore;
        _playerStore = playerStore;
        _itemManager = itemManager;
        _p2pManager = p2pManager;
        _battleZoneCatalog = battleZoneCatalog;
        _simulationMapOverrides = simulationMapOverrides;
    }

    internal bool IsInSimulationRoom(Client client)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock) return pid > 0 && _simulationPidRoom.ContainsKey(pid);
    }

    // HuxleySimulationCenterRule.ActiveGameTypes (and DevActiveGameTypes) in HuxleyMMOGame.ini:
    // the only modes the room menu offers, (1, 5, 4, 2, 9, 8).
    public static bool IsSimulationGameType(byte gameType) => gameType is 1 or 2 or 4 or 5 or 8 or 9;

    // HuxleySimulationCenterRule.CheckStartCondition with HuxleyMMOGame.ini's
    // ActiveGameStartConditions: at least 2 players, and for team modes the larger side may be at
    // most MaxProportion times the smaller (TDM and Hunting 2.0, Radar and CTF 1.3; 0 = no limit).
    // The room menu passes MaxPlayers / 2 as TeamPlayerCount; the INI lists 1, 2, 4, 6 and 8.
    // Retail lets a type without conditions start, but its menu never offers one; here such a
    // room would be a one-player match that settles rewards, so it does not start.
    public static bool CheckStartCondition(byte gameType, int maxPlayers, IEnumerable<byte> tribes)
    {
        float? maxProportion = gameType switch
        {
            1 or 8 => 2.0f,
            2 or 4 => 1.3f,
            5 or 9 or 10 => 0.0f,
            _ => null
        };
        if (maxProportion is not { } proportion) return false;
        if (maxPlayers / 2 is not (1 or 2 or 4 or 6 or 8)) return true;
        int sapiens = tribes.Count(x => x is 1 or 2), alternatives = tribes.Count(x => x is 3 or 4);
        if (sapiens + alternatives < 2) return false;
        return proportion <= 0 || sapiens * proportion >= alternatives && alternatives * proportion >= sapiens;
    }

    public static bool IsSimulationSquadBalanced(IEnumerable<byte> squads)
    {
        int squad0 = 0, squad1 = 0;
        foreach (byte squad in squads)
        {
            if (squad == 0) squad0++;
            else if (squad == 1) squad1++;
            else return false;
        }
        return squad0 > 0 && squad0 == squad1;
    }

    // The listen host sends op109 only after the guest's UE3 login: state 1 during warmup,
    // 2 once play begins. Ignoring state 1 would resend op157/op156 at MatchInProgress and
    // make the guest browse the same world twice.
    public static bool IsSimulationGuestAdmissionComplete(byte state) => state is 1 or 2;

    // HuxleySCRoomMenu: species is ETribeType (Tribe_One/Syn/Alterlabor/Alternix = 1..4, either
    // side may be played in the Simulation Center), squads cycle modulo MAX_SQUAD_NUM = 5, and the
    // busy report uses SC_PLAYER_STATE_NOT_READY/READY/PLAY/BUSY (0, 1, 2, 4).
    public static bool IsSimulationSpecies(byte species) => species is >= 1 and <= 4;

    // HuxleySCRoomMenu.MAX_SQUAD_PLAYER_NUM: players per squad on one side.
    internal const int SIMULATION_SQUAD_MAX = 4;

    public static bool IsSimulationSquad(byte squad) => squad < 5;

    public static bool IsSimulationPlayerState(byte state) => state is 0 or 1 or 2 or 4;

    // Time-limited Skirmish rooms (GoalScore 0): AdjustDefaultReward gives every player one
    // Battle Medal (+1 at level class S) and 5 Lunarite; AdjustWinTeamReward gives winners +1 medal
    // and +2 Lunarite and the best player +1 Lunarite. Medals are not in the result packet;
    // the reported Lunarite already includes these amounts.
    public IReadOnlyList<(int Item, int Count)> SkirmishRewardItems(bool won, int level, int reportedLunarOre)
    {
        int medals = 1 + (CharacterManager.GetLevelClass(level) >= 3 ? 1 : 0) + (won ? 1 : 0);
        int lunarite = Math.Clamp(reportedLunarOre, 0, 5 + 2 + 1);
        var items = new List<(int, int)> { (BattleManager.BattleMedalItem, medals) };
        if (lunarite > 0) items.Add((BattleServerManager.LunariteItem, lunarite));
        return items;
    }

    internal void GrantSkirmishRewards(BattleResultRequest report, int level)
    {
        var items = SkirmishRewardItems(report.Won, level, report.LunarOre);
        if (!_playerStore.TryAddQuestItems(report.Pid, items, out var inserted))
        {
            _logger.LogWarning("[BattleResult] PID {0} could not receive Skirmish rewards (inventory full?).", report.Pid);
            return;
        }
        ItemManager.PublishLiveInventoryInserts(report.Pid, inserted);
        _logger.LogInformation("[BattleResult] PID {0} Skirmish rewards: {1}.", report.Pid,
            string.Join(", ", items.Select(x => $"{x.Item}x{x.Count}")));
    }

    internal int RoomCount => _simulationRooms.Count;

    internal void AssignPort(Client client)
    {
        lock (_simulationLock) EnsureSimulationClientPortLocked(client.Id);
    }

    internal void ReleasePort(Client client)
    {
        lock (_simulationLock) _simulationClientPorts.Remove(client.Id);
    }

    internal bool IsInRoom(int pid)
    {
        lock (_simulationLock) return _simulationPidRoom.ContainsKey(pid);
    }

    // op114 is valid only from the listen host of a match it just ended, for a member of that
    // match, once per member (HuxleyBattleGame.EndGame sends results only on the listen server).
    // timeLimitSeconds: the limit the match started with. joinedAtSeconds: when the member's
    // login completed (0 without an op109 report: the host controls that report either way, and a
    // member it never admitted gets no op114 from an honest host). ranFullTime: the match lasted
    // that limit, as a time-limited (GoalScore 0) match must before EndGame pays its Battle
    // Medals and Lunarite.
    internal bool TryClaimMatchResult(int hostPid, int pid, out int seconds, out ushort goalScore,
        out int timeLimitSeconds, out int joinedAtSeconds, out bool ranFullTime)
    {
        seconds = 0;
        goalScore = ushort.MaxValue;
        timeLimitSeconds = 0;
        joinedAtSeconds = 0;
        ranFullTime = false;
        lock (_simulationLock)
        {
            if (!_finishedSimulationMatches.TryGetValue(hostPid, out var match)) return false;
            if (DateTime.UtcNow - match.FinishedAtUtc > SimulationResultWindow)
            {
                _finishedSimulationMatches.Remove(hostPid);
                return false;
            }
            if (!match.Unsettled.Remove(pid)) return false;
            if (match.Unsettled.Count == 0) _finishedSimulationMatches.Remove(hostPid);
            seconds = match.Seconds;
            goalScore = match.GoalScore;
            timeLimitSeconds = match.TimeLimitSeconds;
            joinedAtSeconds = match.JoinedAtSeconds.GetValueOrDefault(pid);
            ranFullTime = match.Seconds >= SimulationMinRewardSeconds
                && SimulationSlackedSeconds(match.Seconds) >= match.TimeLimitSeconds;
            return true;
        }
    }

    internal void AddTeamChatRecipients(int senderPid, HashSet<int> recipients)
    {
        lock (_simulationLock)
        {
            if (TryGetSimulationMemberLocked(senderPid, out var room, out var sender))
                recipients.UnionWith(room.Members.Values.Where(x => x.Squad == sender.Squad).Select(x => x.Pid));
        }
    }
}
