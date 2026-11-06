using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void SquadEntryAndWeaponLicenses()
    {
        const int entrySquadPid = 920063;
        ResetCharacter(entrySquadPid);
        _playerStore.GetInventory(entrySquadPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 7, ItemId = 609301, Count = 1 }
        ]);
        _playerStore.AddItems(entrySquadPid, [(609301, 1)]);
        Check(_playerStore.HasQuestEntryItems(EntryItemPid, [(609301, 1)])
            && _playerStore.HasQuestEntryItems(entrySquadPid, [(609301, 1)])
            && _playerStore.TryConsumeQuestEntryItems([EntryItemPid, entrySquadPid], [(609301, 1)], out var squadCells)
            && squadCells[EntryItemPid].SequenceEqual(new byte[] { 5 })
            && squadCells[entrySquadPid].SequenceEqual(new byte[] { 7 })
            && !_playerStore.GetInventory(EntryItemPid, () => []).Any(x => x.ItemId == 609301)
            && !_playerStore.GetInventory(entrySquadPid, () => []).Any(x => x.ItemId == 609301),
            "squad quest entry validates and consumes every member's requirements atomically");
        Check(_playerStore.TryExchangeQuestItems(EntryItemPid, [], [(609301, 1)],
                out var restoredEntryA, out var restoredCellsA)
            && _playerStore.TryExchangeQuestItems(entrySquadPid, [], [(609301, 1)],
                out var restoredEntryB, out var restoredCellsB)
            && restoredEntryA.Single().ItemId == 609301 && restoredCellsA.Count == 1
            && restoredEntryB.Single().ItemId == 609301 && restoredCellsB.Count == 1
            && _playerStore.GetItems(EntryItemPid).Single(x => x.item == 609301).num == 1
            && _playerStore.GetItems(entrySquadPid).Single(x => x.item == 609301).num == 1,
            "failed quest travel restores every party member's reserved entry items atomically");
        const int rewardCapacityPid = 920062;
        ResetCharacter(rewardCapacityPid);
        _playerStore.GetProgress(rewardCapacityPid).InvenExpandStep = 2;
        _playerStore.GetInventory(rewardCapacityPid, () => Enumerable.Range(0, 78)
            .Select(i => new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = (byte)i, ItemId = 604221, Count = 1 }).ToList());
        Check(!_playerStore.TryAddQuestItems(rewardCapacityPid, [(604222, 1)], out var fullRewardInsert)
            && fullRewardInsert.Count == 0
            && _playerStore.GetInventory(rewardCapacityPid, () => []).Count == 78
            && _playerStore.GetItems(rewardCapacityPid).Count == 0,
            "full retail MAIN inventory rejects quest rewards without changing existing items");
        Check(_licenseCatalog.Get(710021) is { LicensePoint: 1, PrecedingLicenseIndex: 700020,
                EquipLevel: 10, CombatStyle: "CombatStyle_Enforcer", PermissionLevelClass: "LevelClass_B" } flingerB
            && flingerB.WeaponGroups.SequenceEqual(new[] { "EWG_Flinger" })
            && _licenseCatalog.All.Count >= 200,
            "exported license catalog resolves retail prerequisite, LP, level, style, weapon group, and level class");
        _licenseStore.Reset(920008);
        Check(_licenseStore.Grant(920008, 710021) && _licenseStore.Has(920008, 710021)
            && _licenseStore.Owned(920008).SequenceEqual(new[] { 710021 }),
            "per-character license ownership persists from the authoritative catalog");
        Check(_itemCatalog.Get(100200) is { WeaponGroup: "EWG_Flinger", EquipLevel: 1 } starterFlinger
            && _licenseStore.HaveWeaponLicense(920008, starterFlinger),
            "weapon-license coverage mirrors the retail weapon-group and permission-level-class comparison");
        _equipPid = _ownedA.Pid;
        _characterStore.SetStyle(_equipPid, 1);
        ResetCharacter(_equipPid);
        _licenseStore.Reset(_equipPid);
        _equipFlinger = _itemCatalog.Get(100200)!;
        _equipSource = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 100200, Durability = 100 };
        Check(_equipmentRules.ValidateWeapon(_equipPid, 0, 4, _equipSource, _equipFlinger) == 22,
            "weapon equip rejects a missing matching license with native item result 22");
        Check(_licenseStore.Grant(_equipPid, 700020)
            && _equipmentRules.ValidateWeapon(_equipPid, 0, 4, _equipSource, _equipFlinger) == 0,
            "weapon equip accepts the exact group and sufficient C/B/A/S license coverage");
        _characterStore.SetStyle(_equipPid, 2);
        Check(_equipFlinger.ExclusiveCombatStyle == "CombatStyle_None"
            && _equipmentRules.ValidateWeapon(_equipPid, 0, 4, _equipSource, _equipFlinger) == 0,
            "nonexclusive sub-S weapon follows retail cross-style equip behavior");
        var exclusiveDischarger = _itemCatalog.Get(120301)!;
        _playerStore.GetProgress(_equipPid).BP = PlayerStore.ComputePlayerLevelupGoalBP(19) + 1;
        _licenseStore.Grant(_equipPid, 710031);
        Check(exclusiveDischarger.ExclusiveCombatStyle == "CombatStyle_Enforcer"
            && _equipmentRules.ValidateWeapon(_equipPid, 0, 4,
                new PlayerStore.GenericItem { ItemId = 120301, Durability = 100 }, exclusiveDischarger) == 15,
            "exclusive weapon family rejects cross-style equip with native item result 15");
        _characterStore.SetStyle(_equipPid, 1);
        var pcRoomWeapon = _itemCatalog.Get(109201)!;
        Check(pcRoomWeapon.WeaponGroup.Length != 0
            && _equipmentRules.ValidateWeapon(_equipPid, 0, 4,
                new PlayerStore.GenericItem { ItemId = 109201, Durability = 100 }, pcRoomWeapon) == 48,
            "weapon equip rejects PC-room-only equipment with native item result 48");
        _serverEvents.ResetForTest();
        _serverEvents.SetPcRoom(true);
        byte pcRoomEventEquip = _equipmentRules.ValidateWeapon(_equipPid, 0, 4,
            new PlayerStore.GenericItem { ItemId = 109201, Durability = 100 }, pcRoomWeapon);
        _serverEvents.SetPcRoom(false);
        Check(pcRoomEventEquip != 48 && _serverEvents.BlocksPcRoomItem(pcRoomWeapon)
            && !_serverEvents.BlocksPcRoomItem(_itemCatalog.Get(110101)),
            "a PC-room event lifts the EISC_PCRoom equip refusal; turning it off restores retail rules");
        _serverEvents.ResetForTest();
        _equipSource.Durability = 0;
        Check(_equipmentRules.ValidateWeapon(_equipPid, 0, 4, _equipSource, _equipFlinger) == 1
            && _equipmentRules.ValidateWeapon(_equipPid, 4, 4, _equipSource, _equipFlinger) == 5,
            "weapon equip rejects broken items and out-of-range weapon slots before mutation");
        _equipSource.Durability = 100;
    }

    private void OfflineSquadSetup()
    {
        // Both squad PIDs deliberately use this one test socket: two op97 packets prove
        // fanout to both member entries without launching either retail client.
        _squadManager.SetSquad(920007, OfflineHostPid, [OfflineHostPid, OfflineGuestPid]);
        _questSocketClient.Player = _playerManager.Get(OfflineGuestPid);
        ResetCharacter(VendorGatePid);
        _playerStore.GetInventory(VendorGatePid, () => []);
        _playerStore.AddMoney(VendorGatePid, 10_000);
        _questSocketClient.Player = _playerManager.Get(VendorGatePid);
    }

    private void SquadOfflineMembersPruned()
    {
        // Squad 920090: 990001 never comes back, 990002 stays online. Squad 920091: both members gone.
        const int gone = 990001, online = 990002, goneA = 990003, goneB = 990004;
        _squadManager.SetSquad(920090, gone, [gone, online]);
        _squadManager.SetSquad(920091, goneA, [goneA, goneB]);
        bool IsOnline(int pid) => pid == online;
        long start = 1_000_000, grace = (long)SquadManager.OfflineMemberGrace.TotalMilliseconds;
        var early = _squadManager.PruneOfflineMembers(start, IsOnline);
        var stillEarly = _squadManager.PruneOfflineMembers(start + grace - 1, IsOnline);
        Check(early.Count == 0 && stillEarly.Count == 0
            && _squadManager.SquadMembersOf(gone).SequenceEqual(new[] { gone, online })
            && _squadManager.SquadMembersOf(goneA).SequenceEqual(new[] { goneA, goneB }),
            "squad members offline for less than the grace period (expected travel) keep their places");
        var late = _squadManager.PruneOfflineMembers(start + grace, IsOnline);
        Check(late.SequenceEqual(new[] { online })
            && _squadManager.SquadMembersOf(online).SequenceEqual(new[] { online })
            && _squadManager.SquadOf(online).LeaderPid == online
            && _squadManager.SquadMembersOf(goneA).Count == 0 && _squadManager.SquadMembersOf(goneB).Count == 0,
            "members offline past the grace period leave, leadership passes on, and a squad left empty is deleted");
        _squadManager.RemoveSquad(920090);
    }

    private void SquadLfsAndChat()
    {
        Check(SearchDirectory(0, "Alpha") == "Alpha" && SearchDirectory(0, null) == "Alpha"
            && SearchDirectory(1, "Bravo") == "Bravo" && SearchDirectory(1, null) == "Bravo"
            && SearchDirectory(0, "") == null && SearchDirectory(1, null) == "Bravo"
            && SearchDirectory(1, "") == null,
            "squad and LFS page requests retain independent search filters until cleared");
        Check(new SquadActionResult { Opcode = 101, Answer = 0 }.Serialize().SequenceEqual(new byte[] { 101, 0 })
            && new SquadActionResult { Opcode = 102, Answer = 1 }.Serialize().SequenceEqual(new byte[] { 102, 1 })
            && new SquadActionResult { Opcode = 103, Answer = 0 }.Serialize().SequenceEqual(new byte[] { 103, 0 }),
            "squad create/join/secession callbacks use native one-byte answers");
        Check(new SquadApplication { Pid = 7, Name = "A", Style = 2, Level = 3, SquadKey = 9 }.Serialize()
                .SequenceEqual(new byte[] { 104, 7, 0, 0, 0, 65, 0, 2, 3, 0, 9, 0, 0, 0 })
            && new SquadApplicationAnswer { Answer = 1, LeaderName = "B", SquadName = "C" }.Serialize()
                .SequenceEqual(new byte[] { 105, 1, 66, 0, 67, 0 }),
            "squad application and answer serialize native dsbwd/bss layouts");
        var squadMembers = new SquadMemberList();
        squadMembers.Members.Add(new SquadMember("A", 7, 0, 2, "B", 3, 1, 5, 10, 0));
        Check(squadMembers.Serialize().SequenceEqual(new byte[] { 100, 1, 65, 0, 7, 0, 0, 0, 0, 2, 66, 0,
                3, 0, 1, 5, 10 }.Concat(new byte[16])),
            "squad member list serializes native sdbbswbbbdwdwd layout");
        var lfsList = new LFSList { PageDirection = 1, CurrentPage = 1, TotalPages = 1 };
        lfsList.Entries.Add(new LfsEntry(7, "A", 12, 2, 3));
        Check(lfsList.Serialize().SequenceEqual(new byte[] { 110, 1, 1, 1, 1, 7, 0, 0, 0, 65, 0, 12, 2, 3 })
            && new LFSActionResult { Opcode = 111, Answer = 0 }.Serialize().SequenceEqual(new byte[] { 111, 0 })
            && new LFSPlayerInfo { Answer = 0, Entry = new LfsEntry(7, "A", 12, 2, 3) }.Serialize()
                .SequenceEqual(new byte[] { 113, 0, 7, 0, 0, 0, 65, 0, 12, 2, 3 })
            && new LFSInvite { Answer = 0, SquadKey = 9, SquadName = "B" }.Serialize()
                .SequenceEqual(new byte[] { 114, 0, 9, 0, 0, 0, 66, 0 })
            && new LFSInviteAnswer { Answer = 1, Pid = 7, PlayerName = "A" }.Serialize()
                .SequenceEqual(new byte[] { 115, 1, 7, 0, 0, 0, 65, 0 }),
            "LFS directory, callbacks, lookup, and invitation packets match native layouts");
        Check(SquadManager.LfsInvitationKey(1234, 88) == (1234, 88),
            "LFS invitation consent is keyed by native squad key and target PID, not leader PID");
        Check(_squadManager.AcceptsSquadApplications(0) && !_squadManager.AcceptsSquadApplications(1)
            && !_squadManager.AcceptsSquadApplications(3),
            "private squad option blocks unsolicited join applications");
        Check(SquadManager.BuildSquadJoinFailure().Serialize().SequenceEqual(new byte[] { 102, 1 }),
            "undeliverable squad applications return the native join-failure callback");
        Check(SquadManager.CanInviteToSquad(10, 20, true, false, 3)
            && !SquadManager.CanInviteToSquad(10, 10, true, false, 1)
            && !SquadManager.CanInviteToSquad(10, 20, false, false, 1)
            && !SquadManager.CanInviteToSquad(10, 20, true, true, 1)
            && !SquadManager.CanInviteToSquad(10, 20, true, false, 4),
            "direct squad invitations require a distinct online unsquadded target and capacity");
        Check(_squadManager.BuildLFSInviteFailure(7, false).Serialize()
                .SequenceEqual(new byte[] { 115, 1, 7, 0, 0, 0, 76, 105, 109, 105, 116, 52, 0 })
            && _squadManager.BuildLFSInviteFailure(7, true).Answer == 2,
            "failed direct squad invitations answer the inviter and preserve the blocked result");
        Check(SquadManager.CanRelaySquadNotify(10, 20, new[] { 10, 20 })
            && !SquadManager.CanRelaySquadNotify(10, 30, new[] { 10, 20 })
            && !SquadManager.CanRelaySquadNotify(0, 20, new[] { 10, 20 }),
            "squad notifications remain inside authoritative membership");
        Check(SquadManager.ValidateSquadCreate(10, "Alpha 2", false, false) == 0
            && SquadManager.ValidateSquadCreate(10, "[Kestrel] Squad", false, false) == 0
            && SquadManager.ValidateSquadCreate(10, "", false, false) == 18
            && SquadManager.ValidateSquadCreate(10, "Alpha!", false, false) == 19
            && SquadManager.ValidateSquadCreate(10, "Alpha", true, false) == 8
            && SquadManager.ValidateSquadCreate(10, "Alpha", false, true) == 5,
            "squad creation returns retail name, membership, and duplicate result codes");
        Check(CharacterManager.LevelClassForLevel(1) == 0 && CharacterManager.LevelClassForLevel(9) == 0
            && CharacterManager.LevelClassForLevel(10) == 1 && CharacterManager.LevelClassForLevel(19) == 1
            && CharacterManager.LevelClassForLevel(20) == 2 && CharacterManager.LevelClassForLevel(29) == 2
            && CharacterManager.LevelClassForLevel(30) == 3 && CharacterManager.LevelClassForLevel(99) == 3,
            "LFS level class follows exported retail C/B/A/S thresholds");
        Check(new Chat { Type = 5, From = "A", To = "B", Message = "C" }.Serialize()
                .SequenceEqual(new byte[] { 25, 5, 65, 0, 66, 0, 67, 0 }),
            "server chat serializes native bsss sender/recipient/message order");
        Check(new ChatBlocked().Serialize().SequenceEqual(new byte[] { 198 }),
            "server chat flood warning invokes native parameterless op198 callback");
        Check(new UndoItemInsert { Serial = 4, ItemId = 100100, Count = 2, Price = 300 }.Serialize()
                .SequenceEqual(new byte[] { 195, 4, 4, 135, 1, 0, 2, 0, 44, 1, 0, 0 })
            && new UndoItemRemove { Serial = 4 }.Serialize().SequenceEqual(new byte[] { 196, 4 })
            && new UndoItemResult { Result = 14 }.Serialize().SequenceEqual(new byte[] { 197, 14 }),
            "vendor undo insert/remove/result callbacks match native op195/op196/op197 layouts");
        Check(new UpdateClanHeadLine { Pid = 7, ClanName = "A", Rank = 3 }.Serialize()
                .SequenceEqual(new byte[] { 215, 7, 0, 0, 0, 65, 0, 3 }),
            "clan headline mutation matches native op215 dsb layout");
        Check(ChatManager.ReceiveChatType(0) == 1
            && ChatManager.ReceiveChatType(2) == 3
            && ChatManager.ReceiveChatType(4) == 5
            && ChatManager.ReceiveChatType(6) == 5
            && ChatManager.ReceiveChatType(7) == 8
            && ChatManager.ReceiveChatType(9) == 10
            && ChatManager.ReceiveChatType(15) == 16
            && ChatManager.ReceiveChatType(21) == 22
            && ChatManager.ReceiveChatType(23) == 24
            && ChatManager.ReceiveChatType(1) == byte.MaxValue
            && ChatManager.ReceiveChatType(17) == byte.MaxValue,
            "chat routing maps only exported client send types to native receive types");
        Check(ChatManager.ShoutReaches(1, 6) && ChatManager.ShoutReaches(11, 16)
            && !ChatManager.ShoutReaches(1, 11) && !ChatManager.ShoutReaches(0, 1) && !ChatManager.ShoutReaches(6, 0),
            "shout reaches every online player of the sender's faction, not just the same zone");
        Check(CityManager.SharesPresence(0, 10, 0, 10)
            && !CityManager.SharesPresence(0, 10, 0, 11)
            && CityManager.SharesPresence(820100, 10, 820100, 11)
            && !CityManager.SharesPresence(820100, 10, 820101, 10)
            && !CityManager.SharesPresence(820100, 10, 0, 10),
            "presence scope separates city maps and quest instances without cross-scope leakage");
        Check(_questManager.ResolveQuestExit(14, 3, 10) == (14, 3)
            && _questManager.ResolveQuestExit(-1, -1, 10) == (10, 0)
            && _questManager.ResolveQuestExit(-1, 7, 110) == (110, 0)
            && _questManager.ResolveQuestExit(77, 2, 110) == (110, 0)
            && _questManager.ResolveQuestExit(-1, -1, 110, -1, 13) == (110, 13)
            && _questManager.ResolveQuestExit(-1, -1, 110, 10, 4) == (10, 4)
            && _questManager.ResolveQuestExit(14, 3, 110, 10, 4) == (14, 3),
            "quest leave preserves explicit retail city zone/area and uses faction camp area zero for sentinel requests");
        Check(QuestInstanceManager.QuestDecisionRecipients(7, [7]).Count == 0
            && QuestInstanceManager.QuestDecisionRecipients(7, [7, 8, 9]).SequenceEqual(new[] { 8, 9 }),
            "only the other squad members get the 'enter with your squad leader?' prompt; solo players get none");
        Check(_questManager.IsFreeQuestObjectiveReportAuthorized(7, 846004, 846004, 7, 820706, acquired: true)
            && _questManager.IsFreeQuestObjectiveReportAuthorized(8, 8, 846004, 7, 820706, acquired: true)
            && !_questManager.IsFreeQuestObjectiveReportAuthorized(7, 846004, 846004, 7, 820706, acquired: false)
            && !_questManager.IsFreeQuestObjectiveReportAuthorized(7, 846004, 846004, 0, 820706, acquired: true)
            && !_questManager.IsFreeQuestObjectiveReportAuthorized(7, 999, 846004, 7, 820706, acquired: true)
            && !_questManager.IsFreeQuestObjectiveReportAuthorized(7, 846004, 846004, 7, 820300, acquired: true),
            "field-instance kill reports for an acquired FreeQuest (e.g. 820706 Diseased Covoxes) are accepted from the instance");
        Check(_questManager.ResolveQuestExit(-1, -1, 10, departure: (-1, 0)) == (10, 0)
            && _questManager.ResolveQuestExit(-1, -1, 10, departure: (0, 40)) == (0, 40)
            && _questManager.ResolveQuestExit(-1, -1, 110, -1, 13, departure: (100, 25)) == (100, 13)
            && _questManager.ResolveQuestExit(-1, -1, 10, departure: (8, 0)) == (10, 0)
            && _questManager.ResolveQuestExit(-1, -1, 10, 110, 4, departure: (0, 40)) == (110, 4)
            && _questManager.ResolveQuestExit(14, 2, 10, departure: (0, 40)) == (14, 2),
            "Return to City goes back to the city the mission was started from unless the quest names an exit");
        Check(_questManager.MergeQuestTravelRefresh(0, acquire: true, availability: false) == 1
            && _questManager.MergeQuestTravelRefresh(1, acquire: false, availability: true) == 3
            && _questManager.MergeQuestTravelRefresh(3, acquire: true, availability: true) == 3,
            "post-travel quest refreshes merge without dropping either authoritative list");
        Check(QuestManager.IsQuestAbortLeave(820100) && !QuestManager.IsQuestAbortLeave(0),
            "BRT leave returns an active quest to the city without affecting non-quest BRT cleanup");
    }
}
