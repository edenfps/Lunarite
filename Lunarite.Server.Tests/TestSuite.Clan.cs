using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void ClanMembership()
    {
        Check(new ClanActionResult { Opcode = 176, Result = 0 }.Serialize().SequenceEqual(new byte[] { 176, 0 })
            && new ClanActionResult { Opcode = 183, Result = 21 }.Serialize().SequenceEqual(new byte[] { 183, 21 })
            && new ClanInvite { Result = 7, MasterName = "A", ClanName = "B" }.Serialize()
                .SequenceEqual(new byte[] { 179, 7, 65, 0, 66, 0 })
            && new ClanEntry { MemberName = "A" }.Serialize()
                .SequenceEqual(new byte[] { 192, 65, 0 }),
            "clan create/duplicate/invite and member-entry packets match native branches");
        Check(new ClanChoiceVeteran { MessageIndex = 46 }.Serialize().SequenceEqual(new byte[] { 194, 46 })
            && new ClanChoiceVeteran { MessageIndex = 47 }.Serialize().SequenceEqual(new byte[] { 194, 47 }),
            "clan veteran promotion/demotion notification matches native op194 byte layout");
        Check(new DeletePlayer { Answer = DeletePlayer.ClanMasterMustEntrust }.Serialize()
                .SequenceEqual(new byte[] { 13, 5 }),
            "clan-master deletion refusal uses native result 5 without a success payload");
        var clanMembersPacket = new ClanMember { Result = 13 };
        clanMembersPacket.Members.Add(new ClanMemberWire(7, "A", 2, 1, 3, 9, 1, 4, 110, 0, 1, 4));
        Check(new ClanInfo { Result = 16, ClanName = "A", MasterName = "B", Notice = "C", Mark = 1,
                Contribution = 2, Level = 3 }.Serialize()
                .SequenceEqual(new byte[] { 180, 16, 65, 0, 67, 0, 66, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3 })
            && clanMembersPacket.Serialize().SequenceEqual(new byte[] { 181, 13, 1, 7, 0, 0, 0, 65, 0,
                2, 1, 3, 9, 1, 4, 110, 0, 0, 0, 0, 1, 4, 0, 0, 0 })
            && new ClanMyInfo { ClanName = "A", Grade = 9, Mark = 2 }.Serialize()
                .SequenceEqual(new byte[] { 185, 65, 0, 9, 2, 0, 0, 0 }),
            "clan info/member/my-info packets match native sssddb, dsbbbbbbdbbd, and sbd layouts");
        Check(ClanManager.ClanMapState(false, false) == 0
            && ClanManager.ClanMapState(true, false) == 2
            && ClanManager.ClanMapState(true, true) == 4,
            "clan roster maps authoritative sessions to exported Off, City, and Quest states");
        const int detachedSocialPid = 920099;
        _questRoomMembers.Detach(detachedSocialPid, 930099);
        Check(_characterManager.IsPlayerOnline(detachedSocialPid) && _questManager.IsPlayerInQuest(detachedSocialPid)
            && ClanManager.ClanMapState(_characterManager.IsPlayerOnline(detachedSocialPid),
                _questManager.IsPlayerInQuest(detachedSocialPid)) == 4,
            "friends and clan rosters retain Quest presence while the city Game socket is detached");
        _questRoomMembers.TryTakeDetached(detachedSocialPid, out _);
        Check(!_characterManager.IsPlayerOnline(detachedSocialPid) && !_questManager.IsPlayerInQuest(detachedSocialPid),
            "friends and clan rosters return to Off after detached quest cleanup");
        Check(ClanManager.CanShareClan(1, 6) && ClanManager.CanShareClan(11, 16)
            && !ClanManager.CanShareClan(1, 11) && !ClanManager.CanShareClan(16, 6),
            "clan invitations enforce the retail same-faction result-52 boundary");
        Check(new ClanAppointmentInfo { Result = 37, SubMaster = 1, SquadMaster = 2, SubSquadMaster = 3 }
                .Serialize().SequenceEqual(new byte[] { 189, 37, 1, 2, 3 })
            && new ClanPermissionInfo { Result = 39, Permissions = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9] }
                .Serialize().SequenceEqual(new byte[] { 191, 39, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }),
            "clan appointment and permission info packets match native conditional byte arrays");
        const int clanLeader = 920030;
        const int clanMember = 920031;
        const int clanRecruit = 920032;
        const int clanBoundaryPid = 920033;
        const int clanPoorPid = 920034;
        const int clanFundedPid = 920035;
        _clanStore.Leave(clanPoorPid);
        _clanStore.Leave(clanFundedPid);
        ResetCharacter(clanPoorPid);
        ResetCharacter(clanFundedPid);
        _playerStore.AddReward(clanFundedPid, 0, 0,
            ClanStore.CreationCost - _playerStore.GetProgress(clanFundedPid).Money);
        Check(_clanStore.Create(clanPoorPid, "PoorClan920034", ClanStore.CreationCost) == 2
            && _clanStore.ForPid(clanPoorPid) == null
            && _clanStore.Create(clanFundedPid, "PaidClan920035", ClanStore.CreationCost) == 0
            && _playerStore.GetProgress(clanFundedPid).Money == 0
            && _clanStore.Leave(clanFundedPid) == 11,
            "clan creation enforces and deducts the archived retail 20,000 CR fee");
        _clanStore.Leave(clanBoundaryPid);
        Check(_clanStore.Create(clanBoundaryPid, new string('N', 16)) == 48
            && _clanStore.Create(clanBoundaryPid, new string('N', 15)) == 0
            && _clanStore.SetNotice(clanBoundaryPid, new string('X', 60)) == 19
            && _clanStore.SetNotice(clanBoundaryPid, new string('X', 61)) == 18
            && _clanStore.Leave(clanBoundaryPid) == 11,
            "clan names and notices enforce the exported INT-client 15/60 character limits");
        _clanStore.Leave(clanRecruit);
        _clanStore.Leave(clanMember);
        _clanStore.Leave(clanLeader);
        Check(_clanStore.Create(clanLeader, "TestClan920030") == 0
            && _clanStore.NameExists("testclan920030")
            && _clanStore.Grade(clanLeader) == 9
            && _clanStore.IsMaster(clanLeader)
            && !_clanStore.IsMaster(clanMember),
            "clan creation persists case-insensitive identity and master grade");
        Check(ClanStore.IsConfiguredMark(10001, true)
            && ClanStore.IsConfiguredMark(10034, true)
            && !ClanStore.IsConfiguredMark(20001, true)
            && ClanStore.IsConfiguredMark(20001, false)
            && ClanStore.IsConfiguredMark(20034, false)
            && !ClanStore.IsConfiguredMark(10001, false)
            && ClanStore.IsConfiguredMark(11, true)
            && ClanStore.IsConfiguredMark(164, false)
            && !ClanStore.IsConfiguredMark(0, true)
            && !ClanStore.IsConfiguredMark(165, false),
            "clan emblem validation matches the shipped 96-mark faction/common config");
        _clanStore.Leave(_ownedB.Pid);
        _clanStore.Leave(_ownedA.Pid);
        Check(_clanStore.Create(_ownedA.Pid, "MarkRules") == 0
            && _clanStore.Invite(_ownedA.Pid, _ownedB.Pid) == 7
            && _clanStore.AnswerInvite(_ownedB.Pid, "MarkRules", true) == 6
            && _clanStore.Appoint(_ownedA.Pid, _ownedB.Pid, 3) == 33
            && _clanStore.ForPid(_ownedA.Pid)?.Mark == ClanStore.SapiensDefaultMark
            && _clanStore.SetMark(_ownedB.Pid, 10002) == 51
            && _clanStore.SetPermissions(_ownedA.Pid, [0, 0, 0, 1, 0, 0, 0, 0, 0, 0]) == 38
            && _clanStore.SetMark(_ownedB.Pid, 10002) == 30
            && _clanStore.ForPid(_ownedA.Pid)?.Mark == 10002
            && _clanStore.SetMark(_ownedB.Pid, 20001) == 32
            && _clanStore.SetMark(_ownedB.Pid, 10003, int.MaxValue) == 31
            && _clanStore.Fire(_ownedA.Pid, _ownedB.Pid) == 23
            && _clanStore.Leave(_ownedA.Pid) == 11,
            "new clans get the faction default mark; sub-masters need P3; mark changes cost CR");
        Check(_clanStore.Invite(clanLeader, clanMember) == 7
            && _clanStore.AnswerInvite(clanMember, "TestClan920030", true) == 6
            && _clanStore.ForPid(clanMember)?.Id == _clanStore.ForPid(clanLeader)?.Id
            && !_clanStore.IsMaster(clanMember),
            "clan invitation and acceptance persist shared membership");
        // Accept-time capacity is distinct from invite-time capacity: two pending invitations
        // can race for the final membership slot.
        const int capacityLeader = 920100;
        const string capacityClan = "CapClan920100";
        _clanStore.Leave(capacityLeader);
        bool capacityOk = _clanStore.Create(capacityLeader, capacityClan) == 0;
        for (int i = 0; i < 98 && capacityOk; i++)
        {
            int recruitPid = 920101 + i;
            capacityOk = _clanStore.Invite(capacityLeader, recruitPid) == 7
                && _clanStore.AnswerInvite(recruitPid, capacityClan, true) == 6;
        }
        capacityOk = capacityOk
            && _clanStore.Invite(capacityLeader, 920199) == 7
            && _clanStore.Invite(capacityLeader, 920200) == 7
            && _clanStore.AnswerInvite(920199, capacityClan, true) == 6
            && _clanStore.AnswerInvite(920200, capacityClan, true) == 8
            && _clanStore.ForPid(capacityLeader)?.Members.Count == 100
            && _clanStore.ForPid(920200) == null;
        Check(capacityOk, "clan acceptance enforces the 100-member cap with outstanding invitations");
        Check(_clanStore.Invite(clanMember, clanRecruit) == 51
            && _clanStore.SetNotice(clanMember, "denied") == 51
            && _clanStore.SetMark(clanMember, 12) == 51,
            "ordinary clan members cannot use delegated officer actions");
        Check(_clanStore.Fire(clanMember, clanLeader) == 22 && _clanStore.Fire(clanMember, clanRecruit) is 22 or 25
            && !ClanStore.IsValidClanName("bad	name") && !ClanStore.IsValidClanName("")
            && ClanStore.IsValidClanName("Good Clan-1"),
            "clan kick distinguishes missing members from missing permission; names use the edit-box charset");
        Check(new ClanInvite { Result = 50, PlayerName = "A" }.Serialize().SequenceEqual(new byte[] { 179, 50, 65, 0 })
            && new ClanInvite { Result = 7 }.Serialize().SequenceEqual(new byte[] { 179, 7, 0, 0 })
            && new ClanNotice { Result = 19, Notice = "Hi" }.Serialize().SequenceEqual(new byte[] { 182, 19, 72, 105, 0 })
            && new ClanNotice { Result = 51 }.Serialize().SequenceEqual(new byte[] { 182, 51 }),
            "clan invite and notice packets carry the names and text ProcClanInvite/ProcClanNotice read");
        Check(ClanManager.ClanMapState(false, false) == 0 && ClanManager.ClanMapState(true, false) == 2
            && ClanManager.ClanMapState(true, true) == 4 && ClanManager.ClanMapState(true, false, inBattle: true) == 3
            && ClanManager.ClanMapState(true, false, inSimulation: true) == 5,
            "clan roster reports city, quest, battle field and virtual-combat states");
        var openAppointments = _clanStore.AppointmentCounts(clanMember);
        Check(openAppointments == (1, 4, 8)
            && _clanStore.Appoint(clanLeader, clanMember, 3) == 33
            && _clanStore.AppointmentCounts(clanLeader).SubMaster == 0
            && _clanStore.AppointmentCounts(clanMember).SubMaster == 1
            && _clanStore.SetPermissions(clanLeader, [1, 1, 1, 1, 1, 0, 0, 0, 0, 0]) == 38
            && _clanStore.Invite(clanMember, clanRecruit) == 7
            && _clanStore.AnswerInvite(clanRecruit, "TestClan920030", true) == 6
            && _clanStore.SetNotice(clanMember, "Officer notice") == 19
            && _clanStore.ForPid(clanLeader)!.Notice == "Officer notice"
            && _clanStore.Appoint(clanMember, clanRecruit, 1) == 33
            && _clanStore.Fire(clanMember, clanRecruit) == 23
            && _clanStore.ForPid(clanRecruit) == null,
            "appointment info exposes remaining grade capacity and delegated officer actions remain authorized");
        Check(_clanStore.SetPermissions(clanLeader, [2, 0, 0, 0, 0, 0, 0, 0, 0, 0]) == 51,
            "clan permission updates reject non-boolean wire values");
        ResetCharacter(clanMember);
        _playerStore.AddItems(clanMember, [(609001, 4), (609101, 3)]);
        _playerStore.GetInventory(clanMember, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 5, ItemId = 609001, Count = 4 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 6, ItemId = 609101, Count = 3 }
        ]);
        int contributionBefore = _clanStore.ForPid(clanMember)!.Contribution;
        Check(_clanStore.Contribute(clanMember, 609001, 2, 609101, 1, out var contributionCells) == 40
            && contributionCells.Order().SequenceEqual(new byte[] { 5, 6 })
            && _clanStore.ForPid(clanMember)!.Contribution == contributionBefore + 3
            && _clanStore.ForPid(clanMember)!.Members.Single(x => x.Pid == clanMember).Contribution == 3
            && _playerStore.GetItems(clanMember).Single(x => x.item == 609001).num == 2
            && _playerStore.GetInventory(clanMember, () => []).Single(x => x.ItemId == 609101).Count == 2,
            "clan contribution atomically consumes exact retail medals and advances contribution");
        int contributionAfter = _clanStore.ForPid(clanMember)!.Contribution;
        Check(_clanStore.Contribute(clanMember, 609001, 99, 609101, 0, out _) == 44
            && _clanStore.Contribute(clanMember, 604221, 1, 609101, 0, out _) == 42
            && _clanStore.Contribute(clanMember, 609001, -1, 609101, 0, out _) == 43
            && _clanStore.Contribute(clanMember, 609001, 0, 609101, 0, out _) == 43
            && _clanStore.ForPid(clanMember)!.Contribution == contributionAfter
            && _playerStore.GetItems(clanMember).Single(x => x.item == 609001).num == 2,
            "rejected clan contribution preserves inventory and clan totals");
        Check(_clanStore.Leave(clanLeader) == 12
            && _clanStore.Leave(clanMember) == 11
            && _clanStore.Leave(clanLeader) == 11,
            "clan master must entrust before leaving members and empty clans are removed");
    }


    private void ClanAppointmentGradeLimits()
    {
        const int leader = 920400, officer = 920401, member = 920402;
        foreach (int pid in new[] { member, officer, leader }) _clanStore.Leave(pid);
        Check(_clanStore.Create(leader, "GradeClan920400") == 0
            && _clanStore.Invite(leader, officer) == 7
            && _clanStore.AnswerInvite(officer, "GradeClan920400", true) == 6
            && _clanStore.Invite(leader, member) == 7
            && _clanStore.AnswerInvite(member, "GradeClan920400", true) == 6
            && _clanStore.Appoint(leader, member, 9) == 36
            && _clanStore.Appoint(leader, member, 4) == 36
            && _clanStore.Appoint(leader, officer, 3) == 33
            && _clanStore.SetPermissions(leader, [1, 1, 1, 1, 1, 0, 0, 0, 0, 0]) == 38
            && _clanStore.Appoint(officer, member, 9) == 36
            && _clanStore.Appoint(officer, member, 3) == 51
            && _clanStore.Appoint(officer, member, 2) == 33
            && !_clanStore.IsMaster(member) && !_clanStore.IsMaster(officer),
            "clan appointment accepts grades 0-3 only and never above the appointing officer");
        foreach (int pid in new[] { member, officer, leader }) _clanStore.Leave(pid);
    }
}
