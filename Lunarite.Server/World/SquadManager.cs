using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.World;

// Squads, squad applications and the LFS (looking for squad) board. All of it is guarded by
// _squadLock; packets are sent after the lock is released.
public sealed class SquadManager
{
    internal sealed class SquadRecord
    {
        public int Key;
        public string Name = "";
        public int Options;
        public int LeaderPid;
        public List<int> Members = [];
    }

    // MAX_SQUAD_PLAYER_NUM.
    private const int MaxMembers = 4;
    private const int SquadOptionClosed = 0x1;

    private readonly CharacterStore _characterStore;
    private readonly PlayerStore _playerStore;
    private readonly PlayerManager _playerManager;
    private readonly BlockStore _blockStore;

    private readonly object _squadLock = new();
    private readonly Dictionary<int, SquadRecord> _squads = [];
    private readonly HashSet<(int SquadKey, int ApplicantPid)> _squadApplications = [];
    private readonly Dictionary<int, string> _lfsEntries = [];
    // SendLFSInviteAnswer echoes the squad key from server op114, not the leader PID.
    // Value: leader PID, for the reply.
    private readonly Dictionary<(int SquadKey, int TargetPid), int> _lfsInvitations = [];
    private int _nextSquadKey = 1;

    public SquadManager(CharacterStore characterStore, PlayerStore playerStore, PlayerManager playerManager,
        BlockStore blockStore)
    {
        _characterStore = characterStore;
        _playerStore = playerStore;
        _playerManager = playerManager;
        _blockStore = blockStore;
    }

    public static (int SquadKey, int TargetPid) LfsInvitationKey(int squadKey, int targetPid) =>
        (squadKey, targetPid);

    // HuxleySquadMakeMenu labels option 0x1 "Private" and sends it as
    // SQUAD_OPTION_CLOSED. Explicit invitations use a separate consent path.
    public bool AcceptsSquadApplications(int options) => (options & SquadOptionClosed) == 0;

    public static bool CanInviteToSquad(int leaderPid, int targetPid, bool targetOnline,
        bool targetAlreadyInSquad, int memberCount) =>
        leaderPid > 0 && targetPid > 0 && leaderPid != targetPid && targetOnline
        && !targetAlreadyInSquad && memberCount < MaxMembers;

    public static bool CanRelaySquadNotify(int senderPid, int targetPid, IReadOnlyCollection<int> members) =>
        senderPid > 0 && targetPid > 0 && members.Contains(senderPid) && members.Contains(targetPid);

    public static byte ValidateSquadCreate(int pid, string? name, bool alreadyMember, bool duplicateName)
    {
        if (pid <= 0) return 1;
        if (string.IsNullOrWhiteSpace(name)) return 18; // "Enter the Squad's name."
        // The radial-menu invite auto-creates "[<leader>] Squad" (INT MsgSimpleSquadTitle),
        // so the brackets the client itself generates must pass.
        if (name.Any(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch) && ch != '[' && ch != ']'))
            return 19;
        if (alreadyMember) return 8;                    // already belongs to another squad
        if (duplicateName) return 5;                    // squad has already been formed
        return 0;
    }

    public static bool ShouldPreserveSquadOnDisconnect(bool expectedQuestTravel,
        bool expectedCityTravel) => expectedQuestTravel || expectedCityTravel;

    public static SquadActionResult BuildSquadJoinFailure() =>
        new() { Opcode = 102, Answer = SquadAnswer.Error };

    // Membership

    private SquadRecord? SquadWithMemberLocked(int pid) =>
        _squads.Values.FirstOrDefault(x => x.Members.Contains(pid));

    private bool IsInSquadLocked(int pid) => _squads.Values.Any(x => x.Members.Contains(pid));

    internal (List<int> Members, int LeaderPid) SquadOf(int pid)
    {
        lock (_squadLock)
        {
            var squad = SquadWithMemberLocked(pid);
            return (squad?.Members.ToList() ?? [pid], squad?.LeaderPid ?? pid);
        }
    }

    internal List<int> SquadMembersOf(int pid)
    {
        lock (_squadLock) return SquadWithMemberLocked(pid)?.Members.ToList() ?? [];
    }

    internal bool AreSquadmates(int pid, int otherPid)
    {
        lock (_squadLock) return _squads.Values.Any(x => x.Members.Contains(pid) && x.Members.Contains(otherPid));
    }

    internal void AddSquadChatRecipients(int senderPid, HashSet<int> recipients)
    {
        lock (_squadLock)
        {
            var squad = SquadWithMemberLocked(senderPid);
            if (squad != null) recipients.UnionWith(squad.Members);
        }
    }

    // Squads

    internal byte SquadCreate(int pid, string? name, int options)
    {
        lock (_squadLock)
        {
            bool duplicateName = _squads.Values.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            byte answer = ValidateSquadCreate(pid, name, IsInSquadLocked(pid), duplicateName);
            if (answer != 0) return answer;
            int key = _nextSquadKey++;
            _squads[key] = new SquadRecord { Key = key, Name = name!, Options = options, LeaderPid = pid, Members = [pid] };
            _lfsEntries.Remove(pid);
            return answer;
        }
    }

    internal (byte Answer, string SquadName) VanSquadMember(int leaderPid, int targetPid)
    {
        lock (_squadLock)
        {
            var squad = _squads.Values.FirstOrDefault(x => x.LeaderPid == leaderPid && x.Members.Contains(targetPid));
            if (squad == null || targetPid == leaderPid) return (SquadAnswer.Error, "");
            squad.Members.Remove(targetPid);
            _squadApplications.RemoveWhere(x => x.ApplicantPid == targetPid);
            return (SquadAnswer.Ok, squad.Name);
        }
    }

    internal byte DelegateLeaderPower(int leaderPid, int targetPid)
    {
        lock (_squadLock)
        {
            var squad = _squads.Values.FirstOrDefault(x => x.LeaderPid == leaderPid && x.Members.Contains(targetPid));
            if (squad == null || targetPid == leaderPid) return SquadAnswer.Error;
            squad.LeaderPid = targetPid;
            return SquadAnswer.Ok;
        }
    }

    internal (List<int> Remaining, byte Answer) SquadSecession(int pid)
    {
        lock (_squadLock)
        {
            var squad = SquadWithMemberLocked(pid);
            if (squad == null) return ([], SquadAnswer.Error);
            RemoveMemberLocked(squad, pid);
            return (squad.Members.ToList(), SquadAnswer.Ok);
        }
    }

    // A dropped link leaves the LFS board and its pending invitations. It leaves its squad too,
    // unless it dropped for expected travel. Returns the members left to notify.
    internal List<int> LeaveOnDisconnect(int pid, bool preserveSquad)
    {
        lock (_squadLock)
        {
            _lfsEntries.Remove(pid);
            foreach (var invitation in _lfsInvitations
                         .Where(x => x.Value == pid || x.Key.TargetPid == pid)
                         .Select(x => x.Key).ToList())
                _lfsInvitations.Remove(invitation);
            var squad = preserveSquad ? null : SquadWithMemberLocked(pid);
            if (squad == null) return [];
            RemoveMemberLocked(squad, pid);
            return squad.Members.ToList();
        }
    }

    // A member kept across an expected travel disconnect stays listed while its link is down. One
    // that is still offline after OfflineMemberGrace leaves the squad, so a squad whose players all
    // crashed or quit mid-travel is deleted instead of lingering with no one in it.
    public static readonly TimeSpan OfflineMemberGrace = TimeSpan.FromMinutes(5);
    private readonly Dictionary<int, long> _offlineSince = [];

    // Returns one remaining member of each squad that lost someone, for the squad-state refresh.
    internal List<int> PruneOfflineMembers(long nowMs, Func<int, bool> isOnline)
    {
        var notify = new List<int>();
        lock (_squadLock)
        {
            foreach (var squad in _squads.Values.ToList())
            {
                bool changed = false;
                foreach (int pid in squad.Members.ToList())
                {
                    if (isOnline(pid)) { _offlineSince.Remove(pid); continue; }
                    if (!_offlineSince.TryGetValue(pid, out long since)) _offlineSince[pid] = since = nowMs;
                    if (nowMs - since < (long)OfflineMemberGrace.TotalMilliseconds) continue;
                    _offlineSince.Remove(pid);
                    RemoveMemberLocked(squad, pid);
                    changed = true;
                }
                if (changed && squad.Members.FirstOrDefault(isOnline) is int remaining and > 0) notify.Add(remaining);
            }
            foreach (int pid in _offlineSince.Keys.Where(x => !IsInSquadLocked(x)).ToList()) _offlineSince.Remove(pid);
        }
        return notify;
    }

    public void SweepOfflineMembers()
    {
        foreach (int memberPid in PruneOfflineMembers(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _playerManager.IsOnline))
            BroadcastSquadState(memberPid);
    }

    private void RemoveMemberLocked(SquadRecord squad, int pid)
    {
        squad.Members.Remove(pid);
        _squadApplications.RemoveWhere(x => x.ApplicantPid == pid);
        if (squad.Members.Count == 0)
        {
            _squads.Remove(squad.Key);
            _squadApplications.RemoveWhere(x => x.SquadKey == squad.Key);
        }
        else if (squad.LeaderPid == pid) squad.LeaderPid = squad.Members[0];
    }

    // Applications

    internal (byte Answer, int LeaderPid) SquadJoin(int pid, int key)
    {
        lock (_squadLock)
        {
            if (pid <= 0) return (SquadAnswer.Error, 0);
            if (IsInSquadLocked(pid)) return (SquadAnswer.OtherSquad, 0);
            if (!_squads.TryGetValue(key, out var squad) || !_playerManager.IsOnline(squad.LeaderPid))
                return (SquadAnswer.NotFound, 0);
            if (squad.Members.Count >= MaxMembers) return (SquadAnswer.TooManyMembers, 0);
            if (!AcceptsSquadApplications(squad.Options)) return (SquadAnswer.Error, 0);
            _squadApplications.Add((key, pid));
            return (SquadAnswer.Ok, squad.LeaderPid);
        }
    }

    internal void WithdrawApplication(int pid, int key)
    {
        lock (_squadLock) _squadApplications.Remove((key, pid));
    }

    // Valid is false when the leader answers an application that is not pending.
    internal (bool Valid, string SquadName, bool Joined, byte JoinFailure) SquadApplicationAnswer(
        int leaderPid, int key, int applicantPid, byte answer)
    {
        lock (_squadLock)
        {
            if (!_squads.TryGetValue(key, out var squad) || squad.LeaderPid != leaderPid
                || !_squadApplications.Remove((key, applicantPid)))
                return (false, "", false, 0);
            if (answer != 0) return (true, squad.Name, false, 0);
            if (IsInSquadLocked(applicantPid)) return (true, squad.Name, false, SquadAnswer.OtherSquad);
            if (squad.Members.Count >= MaxMembers) return (true, squad.Name, false, SquadAnswer.TooManyMembers);
            squad.Members.Add(applicantPid);
            _lfsEntries.Remove(applicantPid);
            return (true, squad.Name, true, 0);
        }
    }

    // LFS board

    internal byte LFSJoin(int pid, string? comment)
    {
        lock (_squadLock)
        {
            if (pid <= 0 || IsInSquadLocked(pid)) return SquadAnswer.Error;
            _lfsEntries[pid] = comment ?? "";
            return SquadAnswer.Ok;
        }
    }

    internal byte LFSSecession(int pid)
    {
        lock (_squadLock)
        {
            byte answer = _lfsEntries.Remove(pid) ? (byte)0 : (byte)1;
            foreach (var key in _lfsInvitations.Keys.Where(x => x.TargetPid == pid).ToList())
                _lfsInvitations.Remove(key);
            return answer;
        }
    }

    internal bool IsInLfs(int pid)
    {
        lock (_squadLock) return _lfsEntries.ContainsKey(pid);
    }

    // Records the leader's invitation of an LFS player. Null when it cannot be sent; Blocked
    // tells the leader the target blocks them.
    internal ((int SquadKey, string SquadName)? Invite, bool Blocked) LFSInvite(int leaderPid, int targetPid)
    {
        lock (_squadLock)
        {
            var squad = _squads.Values.FirstOrDefault(x => x.LeaderPid == leaderPid && x.Members.Count < MaxMembers);
            bool blocked = _blockStore.List(targetPid).Contains(leaderPid);
            if (squad == null || blocked || !CanInviteToSquad(leaderPid, targetPid,
                    _playerManager.IsOnline(targetPid), IsInSquadLocked(targetPid), squad.Members.Count))
                return (null, blocked);
            _lfsInvitations[LfsInvitationKey(squad.Key, targetPid)] = leaderPid;
            return ((squad.Key, squad.Name), false);
        }
    }

    internal void ForgetLfsInvite(int squadKey, int targetPid)
    {
        lock (_squadLock) _lfsInvitations.Remove(LfsInvitationKey(squadKey, targetPid));
    }

    internal (bool Joined, int LeaderPid) LFSInviteAnswer(int targetPid, int squadKey, byte answer)
    {
        lock (_squadLock)
        {
            if (!_lfsInvitations.Remove(LfsInvitationKey(squadKey, targetPid), out int leaderPid)) return (false, 0);
            if (answer != 0 || !_squads.TryGetValue(squadKey, out var squad) || squad.LeaderPid != leaderPid
                || squad.Members.Count >= MaxMembers || IsInSquadLocked(targetPid))
                return (false, leaderPid);
            squad.Members.Add(targetPid);
            _lfsEntries.Remove(targetPid);
            return (true, leaderPid);
        }
    }

    // Directories and state packets

    internal void SendSquadList(Client client, byte pageDirection, string? newSearch)
    {
        string? keyword = CityManager.DirectoryKeyword(client, 0, newSearch);
        var response = new SquadList { PageDirection = 1 };
        lock (_squadLock)
        {
            var all = _squads.Values.Where(x => keyword == null
                || x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Key).ToList();
            int total = (all.Count + Game.DirectoryPageSize - 1) / Game.DirectoryPageSize;
            int page = newSearch == null
                ? CityManager.ResolvePage(pageDirection, client.Session.DirectoryPages.GetValueOrDefault((byte)0), total) : 0;
            client.Session.DirectoryPages[0] = page;
            response.CurrentPage = (byte)page;
            response.TotalPages = (byte)Math.Min(byte.MaxValue, total);
            foreach (var squad in all.Skip(page * Game.DirectoryPageSize).Take(Game.DirectoryPageSize))
                response.Entries.Add(new SquadList.Entry(squad.Key, squad.Name,
                    (byte)Math.Min(byte.MaxValue, squad.Members.Count)));
        }
        client.SendPacket(response);
    }

    internal LfsEntry BuildLFSEntry(int pid)
    {
        var character = _characterStore.Get(pid);
        var progress = _playerStore.GetProgress(pid);
        return new LfsEntry(pid, character?.Name ?? "",
            (byte)Math.Clamp(progress.Level, 0, byte.MaxValue),
            CharacterManager.LevelClassForLevel(progress.Level),
            character?.Style ?? 0);
    }

    internal void SendLFSList(Client client, byte pageDirection, string? keyword)
    {
        string? activeKeyword = CityManager.DirectoryKeyword(client, 1, keyword);
        var response = new LFSList { PageDirection = 1 };
        lock (_squadLock)
        {
            IEnumerable<int> pids = _lfsEntries.Keys;
            if (activeKeyword != null)
                pids = pids.Where(pid => (_characterStore.Get(pid)?.Name ?? "")
                    .Contains(activeKeyword, StringComparison.OrdinalIgnoreCase));
            var matches = pids.Order().ToList();
            int total = (matches.Count + Game.DirectoryPageSize - 1) / Game.DirectoryPageSize;
            int page = keyword == null
                ? CityManager.ResolvePage(pageDirection, client.Session.DirectoryPages.GetValueOrDefault((byte)1), total)
                : 0;
            client.Session.DirectoryPages[1] = page;
            response.CurrentPage = (byte)page;
            response.TotalPages = (byte)Math.Min(byte.MaxValue, total);
            foreach (int pid in matches.Skip(page * Game.DirectoryPageSize).Take(Game.DirectoryPageSize))
                response.Entries.Add(BuildLFSEntry(pid));
        }
        client.SendPacket(response);
    }

    public LFSInviteAnswer BuildLFSInviteFailure(int targetPid, bool blocked) =>
        new()
        {
            Answer = blocked ? (byte)2 : (byte)1,
            Pid = targetPid,
            PlayerName = _characterStore.Get(targetPid)?.Name ?? ""
        };

    internal void BroadcastSquadState(int memberPid)
    {
        foreach (int pid in SquadMembersOf(memberPid))
            if (Game.TryGetClient(pid, out var memberClient)) SendSquadStateAndMembers(memberClient);
    }

    internal void SendSquadStateAndMembers(Client client)
    {
        int pid = Game.PidOf(client);
        string squadName;
        int leaderPid;
        List<int> members;
        bool lfs;
        lock (_squadLock)
        {
            var squad = SquadWithMemberLocked(pid);
            squadName = squad?.Name ?? "";
            leaderPid = squad?.LeaderPid ?? 0;
            members = squad?.Members.ToList() ?? [];
            lfs = _lfsEntries.ContainsKey(pid);
        }
        if (members.Count == 0)
        {
            client.SendPacket(new SquadState { State = lfs ? (byte)2 : (byte)0 });
            client.SendPacket(new EmptySquadMemberList());
            return;
        }

        client.SendPacket(new SquadState { State = (byte)(1 | (leaderPid == pid ? 4 : 0)) });
        var response = new SquadMemberList();
        foreach (int memberPid in members)
        {
            var character = _characterStore.Get(memberPid);
            var progress = _playerStore.GetProgress(memberPid);
            byte tribeRank = (byte)((character?.Index ?? 1) >= 11 ? 5 : 0);
            byte mapIndex = (byte)Math.Clamp(Game.PlayerFor(memberPid).CityMap
                ?? CharacterStore.TrainingCenterMap(character?.Index ?? 1), 0, byte.MaxValue);
            response.Members.Add(new SquadMember(squadName, memberPid, progress.Rank, character?.Style ?? 0,
                character?.Name ?? "", (ushort)Math.Clamp(progress.Level, 0, ushort.MaxValue),
                (byte)(leaderPid == memberPid ? 1 : 0), tribeRank, mapIndex, 0));
        }
        client.SendPacket(response);
    }

    internal void BroadcastSquad(int senderPid, Action<int, Client> send)
    {
        int leaderPid;
        List<int> recipients;
        lock (_squadLock)
        {
            var squad = SquadWithMemberLocked(senderPid);
            if (squad == null) return;
            leaderPid = squad.LeaderPid;
            recipients = squad.Members.Where(x => x != senderPid).ToList();
        }
        foreach (int pid in recipients)
            if (Game.TryGetClient(pid, out var target)) send(leaderPid, target);
    }

    // Places a squad directly, without the create and application handshake.
    internal void SetSquad(int key, int leaderPid, List<int> members)
    {
        lock (_squadLock)
            _squads[key] = new SquadRecord { Key = key, LeaderPid = leaderPid, Members = members };
    }

    internal void RemoveSquad(int key)
    {
        lock (_squadLock) _squads.Remove(key);
    }
}
