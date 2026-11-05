using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

public sealed class ClanStore
{
    private readonly ILogger _logger;
    private readonly CharacterStore _characterStore;
    private readonly PlayerStore _playerStore;

    public const int CreationCost = 20_000;
    // HuxleyClanMarkMenu confirm text: changing the mark costs 20,000 CR (result 31 on shortfall).
    public const int MarkChangeCost = 20_000;
    // HuxleyClanMarkMenu SAPI_DEFAULTMARK / ALTER_DEFAULTMARK.
    public const int SapiensDefaultMark = 10001, AlternativeDefaultMark = 20001;

    public sealed class Member
    {
        public int Pid { get; set; }

        public byte Grade { get; set; }

        public int Contribution { get; set; }
    }

    public sealed class Clan
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public int Mark { get; set; }

        public string Notice { get; set; } = "";

        public int Contribution { get; set; }

        public byte[] Permissions { get; set; } = new byte[10];

        public List<Member> Members { get; set; } = [];
    }

    private static readonly object Lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "clans.json");
    private readonly List<Clan> Clans;
    // Target PID -> (clan, inviter). The inviter is told about a refusal (op179 result 9).
    private static readonly Dictionary<int, (int ClanId, int InviterPid)> Invitations = [];
    private int _nextId;

    private readonly AtomicJson _atomicJson;

    public ClanStore(ILogger<ClanStore> logger, CharacterStore characterStore, PlayerStore playerStore,
        AtomicJson atomicJson)
    {
        _logger = logger;
        _characterStore = characterStore;
        _playerStore = playerStore;
        _atomicJson = atomicJson;
        Clans = Load();
        _nextId = Clans.Count == 0 ? 1 : Clans.Max(x => x.Id) + 1;
    }

    private List<Clan> Load()
    {
        try { return _atomicJson.Read<List<Clan>>(PathName) ?? []; }
        catch (Exception ex) { _logger.LogWarning("[Clans] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try { _atomicJson.Write(PathName, JsonSerializer.Serialize(Clans, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[Clans] save failed: {0}", ex.Message); }
    }

    public Clan? ForPid(int pid)
    {
        lock (Lock) return Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
    }

    public byte Grade(int pid)
    {
        lock (Lock) return Clans.SelectMany(x => x.Members).FirstOrDefault(x => x.Pid == pid)?.Grade ?? 0;
    }

    public bool IsMaster(int pid)
    {
        lock (Lock) return Clans.Any(x => x.Members.Any(m => m.Pid == pid && m.Grade == 9));
    }

    public bool NameExists(string name)
    {
        lock (Lock) return Clans.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // HuxleyClanInfoEditMenu: CHARSET_IncludeSomeSpecial edit box; letters, digits, spaces and
    // ordinary punctuation only.
    public static bool IsValidClanName(string name) =>
        name.Length is > 0 and <= 15
        && name.All(c => char.IsLetterOrDigit(c) || c == ' ' || "-_.'!?()[]".Contains(c));

    public byte Create(int pid, string name, int creationCost = 0)
    {
        // HuxleyClanInfoEditMenu caps INT/non-Korean names at 15 characters. The trimmed form is
        // validated so "Abc " cannot coexist with "Abc".
        name = (name ?? "").Trim();
        if (!IsValidClanName(name)) return 48; // CLAN_INVALIDNAME
        if (creationCost < 0) return 2;
        lock (Lock)
        {
            if (Clans.Any(x => x.Members.Any(m => m.Pid == pid))) return 3; // CREATE_EXIST
            if (Clans.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return 4;
            // The archived retail guide gives a 20,000 CR fee, charged after validation.
            if (!_playerStore.TrySpendMoney(pid, creationCost)) return 2; // CREATE_SHORTMONEY
            int mark = _characterStore.Get(pid)?.Index is >= 11 ? AlternativeDefaultMark : SapiensDefaultMark;
            Clans.Add(new Clan { Id = _nextId++, Name = name, Mark = mark,
                Members = [new Member { Pid = pid, Grade = 9 }] });
            Save(); return 0;
        }
    }

    public byte Invite(int leaderPid, int targetPid)
    {
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == leaderPid));
            var actor = clan?.Members.FirstOrDefault(m => m.Pid == leaderPid);
            if (clan == null || actor == null || !HasPermission(clan, actor, 0, 7, 9))
                return 51; // CLAN_NOPERMISSION
            if (clan.Members.Count >= 100) return 8;
            if (Clans.Any(x => x.Members.Any(m => m.Pid == targetPid))) return 50; // CLAN_JOINED
            Invitations[targetPid] = (clan.Id, leaderPid); return 7;
        }
    }

    public byte AnswerInvite(int targetPid, string clanName, bool accept) =>
        AnswerInvite(targetPid, clanName, accept, out _);

    public byte AnswerInvite(int targetPid, string clanName, bool accept, out int inviterPid)
    {
        inviterPid = 0;
        lock (Lock)
        {
            if (!Invitations.Remove(targetPid, out var invitation)) return 10;
            int clanId = invitation.ClanId;
            inviterPid = invitation.InviterPid;
            var clan = Clans.FirstOrDefault(x =>
                x.Id == clanId && string.Equals(x.Name, clanName, StringComparison.OrdinalIgnoreCase));
            if (!accept) return 9;
            if (clan == null) return 10;
            // The invite carries the inviter's authority at answer time: a fired or demoted
            // inviter's outstanding invitation is no longer valid.
            var inviter = clan.Members.FirstOrDefault(m => m.Pid == invitation.InviterPid);
            if (inviter == null || !HasPermission(clan, inviter, 0, 7, 9)) return 10;
            // Several invitations can be outstanding when the last slot fills.
            if (clan.Members.Count >= 100) return 8;
            if (Clans.Any(x => x.Members.Any(m => m.Pid == targetPid))) return 50;
            clan.Members.Add(new Member { Pid = targetPid, Grade = 0 }); Save(); return 6;
        }
    }

    public byte Leave(int pid)
    {
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
            if (clan == null) return 49; // CLAN_NOJOINED
            var member = clan.Members.First(x => x.Pid == pid);
            if (member.Grade == 9 && clan.Members.Count > 1) return 12; // PREENTRUST
            clan.Members.Remove(member);
            if (clan.Members.Count == 0) Clans.Remove(clan);
            Save(); return 11;
        }
    }

    // Deletion is already refused for clan masters; this removes membership and invitations.
    public bool DeleteCharacter(int pid)
    {
        lock (Lock)
        {
            Invitations.Remove(pid);
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
            if (clan == null) return true;
            var member = clan.Members.First(m => m.Pid == pid);
            if (member.Grade == 9) return false;
            clan.Members.Remove(member);
            if (clan.Members.Count == 0) Clans.Remove(clan);
            Save();
            return true;
        }
    }

    public byte SetNotice(int pid, string notice)
    {
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
            var actor = clan?.Members.FirstOrDefault(m => m.Pid == pid);
            if (clan == null || actor == null || !HasPermission(clan, actor, 1, 8)) return 51;
            // MAXCHARACTERS_CHANGENOTICE in the exported retail menu.
            if (string.IsNullOrWhiteSpace(notice) || notice.Length > 60) return 18;
            clan.Notice = notice; Save(); return 19;
        }
    }

    public byte Fire(int pid, int targetPid)
    {
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
            var actor = clan?.Members.FirstOrDefault(m => m.Pid == pid);
            var target = clan?.Members.FirstOrDefault(m => m.Pid == targetPid && m.Grade != 9);
            if (actor == null || target == null) return 22; // no such member
            if (!HasPermission(clan!, actor, 2) || (actor.Grade != 9 && target.Grade >= actor.Grade))
                return 25; // CLAN_FIRE_NOPERMISSION
            clan!.Members.Remove(target); Save(); return 23;
        }
    }

    public byte Entrust(int pid, int targetPid)
    {
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid && m.Grade == 9));
            var oldMaster = clan?.Members.FirstOrDefault(m => m.Pid == pid);
            var target = clan?.Members.FirstOrDefault(m => m.Pid == targetPid);
            if (oldMaster == null || target == null || target.Pid == pid) return 27;
            oldMaster.Grade = 0; target.Grade = 9; Save(); return 26;
        }
    }

    public byte SetMark(int pid, int mark, int cost = 0)
    {
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
            var actor = clan?.Members.FirstOrDefault(m => m.Pid == pid);
            // HuxleyClanMarkMenu allows ECMG_MASTER and ECMG_SUBMASTER; the permission menu's P3
            // flag delegates it to sub-masters. The shipped config has 16 faction marks per side
            // and 64 common marks.
            if (clan == null || actor == null || actor.Grade is not (9 or 3)
                || !HasPermission(clan, actor, 3)) return 51; // CLAN_NOPERMISSION
            var character = _characterStore.Get(pid);
            bool sapiens = character?.Index is > 0 and < 11;
            if (character == null || !IsConfiguredMark(mark, sapiens)) return 32;
            if (cost < 0 || !_playerStore.TrySpendMoney(pid, cost)) return 31; // not enough CR
            clan.Mark = mark; Save(); return 30;
        }
    }

    public static bool IsConfiguredMark(int mark, bool sapiens)
    {
        // Common TextureIconItem names 00011..00164 become integer IDs 11..164.
        int commonGroup = mark / 10, commonVariant = mark % 10;
        if (commonGroup is >= 1 and <= 16 && commonVariant is >= 1 and <= 4) return true;

        int factionBase = sapiens ? 10_000 : 20_000;
        int local = mark - factionBase;
        int factionGroup = local / 10, factionVariant = local % 10;
        return local > 0 && factionGroup is >= 0 and <= 3 && factionVariant is >= 1 and <= 4;
    }

    public byte Appoint(int pid, int targetPid, byte grade)
    {
        // HuxleyClanAppointmentMenu offers grades 0-3 only; mastership moves through Entrust, so
        // a grade-9 request (ExecSendClanAppointment or a crafted op172) would mint a second master.
        if (grade > 3) return 36;
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
            var actor = clan?.Members.FirstOrDefault(m => m.Pid == pid);
            var target = clan?.Members.FirstOrDefault(m => m.Pid == targetPid && m.Grade != 9);
            if (actor == null || target == null) return 36;
            if (!HasPermission(clan!, actor, 4)
                || (actor.Grade != 9 && (target.Grade >= actor.Grade || grade >= actor.Grade)))
                return 51; // CLAN_NOPERMISSION
            int max = grade switch { 3 => 1, 2 => 4, 1 => 8, _ => 100 };
            if (clan!.Members.Count(m => m.Grade == grade && m.Pid != targetPid) >= max) return 35;
            target.Grade = grade; Save(); return 33;
        }
    }

    public (byte SubMaster, byte SquadMaster, byte SubSquadMaster) AppointmentCounts(int pid)
    {
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
            if (clan == null) return (0, 0, 0);

            // HuxleyClanAppointmentMenu disables a grade at zero, so these are remaining slots, not
            // member counts. The inspected member is excluded so they can keep their own grade at
            // a full cap. Limits match Appoint.
            byte Remaining(byte grade, int maximum) => (byte)Math.Clamp(maximum
                - clan.Members.Count(m => m.Grade == grade && m.Pid != pid), 0, byte.MaxValue);
            return (Remaining(3, 1), Remaining(2, 4), Remaining(1, 8));
        }
    }

    public byte SetPermissions(int pid, byte[] permissions)
    {
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid && m.Grade == 9));
            if (clan == null || permissions.Length != 10 || permissions.Any(x => x > 1)) return 51;
            clan.Permissions = permissions.ToArray(); Save(); return 38;
        }
    }

    public IReadOnlyList<int> MemberPids(int pid)
    {
        lock (Lock)
            return Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid))?.Members
                .Select(m => m.Pid).ToList() ?? [];
    }

    public byte[] GetPermissions(int pid)
    {
        lock (Lock)
        {
            var value = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid))?.Permissions;
            return value is { Length: 10 } ? value.ToArray() : new byte[10];
        }
    }

    // HuxleyClanPermissionMenu maps the ten persisted bytes by rank and action.
    // Masters are always authorized; delegated ranks require their matching flag.
    private static bool HasPermission(Clan clan, Member actor, params int[] permissionByGrade)
    {
        if (actor.Grade == 9) return true;
        int index = actor.Grade switch
        {
            3 when permissionByGrade.Length >= 1 => permissionByGrade[0],
            2 when permissionByGrade.Length >= 2 => permissionByGrade[1],
            1 when permissionByGrade.Length >= 3 => permissionByGrade[2],
            _ => -1
        };
        return index >= 0 && index < clan.Permissions.Length && clan.Permissions[index] == 1;
    }

    public byte Contribute(int pid, int battleMedalId, int battleCount, int guardMedalId,
        int guardCount, out List<byte> changedCells)
    {
        const int RetailA = 609001, RetailB = 609101;
        changedCells = [];
        if (battleMedalId != RetailA || guardMedalId != RetailB) return 42; // INVALIDITEM
        if (battleCount < 0 || guardCount < 0 || battleCount + guardCount <= 0) return 43; // INVALIDCOUNT
        lock (Lock)
        {
            var clan = Clans.FirstOrDefault(x => x.Members.Any(m => m.Pid == pid));
            if (clan == null) return 49;
            if (!_playerStore.TryConsumeQuestEntryItems(pid,
                    [(RetailA, battleCount), (RetailB, guardCount)], out changedCells)) return 44; // NOTEXIT
            int amount = battleCount + guardCount;
            clan.Contribution = checked(clan.Contribution + amount);
            clan.Members.First(x => x.Pid == pid).Contribution =
                checked(clan.Members.First(x => x.Pid == pid).Contribution + amount);
            Save(); return 40;
        }
    }
}
