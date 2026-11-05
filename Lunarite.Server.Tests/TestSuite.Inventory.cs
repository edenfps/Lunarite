using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void AttachmentRemoval()
    {
        _playerStore.AddItems(Sender, [(604221, 3), (100100, 1)]);

        Check(!_playerStore.TryRemoveMainItems(Sender, [4, 4], out _), "duplicate attachment cells rejected");
        Check(!_playerStore.TryRemoveMainItems(Sender, [4, 99], out _), "missing attachment rolls back all cells");
        Check(_playerStore.GetInventory(Sender, () => []).Count == 2, "failed removal leaves inventory unchanged");
        Check(_playerStore.TryRemoveMainItems(Sender, [4, 7, byte.MaxValue], out _sent), "valid attachments removed");
        Check(_sent.Count == 2 && _playerStore.GetInventory(Sender, () => []).Count == 0
            && _playerStore.GetItems(Sender).Count == 0,
            "both selected items leave aggregate and slotted sender persistence");
        ResetCharacter(Sender);
        _playerStore.GetInventory(Sender, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 9, ItemId = 608001, Count = 1, State = 0x02 }
        ]);
        Check(!_playerStore.TryRemoveMainItems(Sender, [9], out _, out bool mailBound) && mailBound
            && _playerStore.GetInventory(Sender, () => []).Single().InventoryCell == 9,
            "mail attachment escrow rejects bound-on-pickup items atomically with TRANSMITRET_ITEM_BIND evidence");
        _playerStore.GetInventory(Sender, () => []).Clear();

        Check(_itemCatalog.Get(109201) is { ItemSalesCategory: 1 },
            "item catalog preserves exported EISC_PCRoom sales category");
        _playerStore.GetInventory(Sender, () => []).Add(
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 10, ItemId = 109201, Count = 1 });
        Check(!_playerStore.TryRemoveMainItems(Sender, [10], out _, out bool pcRoomBound, out bool cannotEnclose)
            && !pcRoomBound && cannotEnclose
            && _playerStore.GetInventory(Sender, () => []).Single().InventoryCell == 10,
            "mail attachment escrow rejects PC-room items atomically with TRANSMITRET_CANT_ENCLOSE evidence");
        _playerStore.GetInventory(Sender, () => []).Clear();

        Check(_playerStore.TrySpendMoney(Sender, 750), "sender can enclose CR");
    }

    private void InventoryExpansionAndTimers()
    {
        ResetCharacter(ExpandPid);
        _playerStore.GetInventory(ExpandPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 674001, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 675001, Count = 3 }
        ]);
        Check(_itemCatalog.Get(674001) is { GeneralValue: 10 }
            && _itemCatalog.Get(675001) is { GeneralValue: 10 }
            && _playerStore.MainCapacity(ExpandPid) == 48 && _playerStore.BankCapacity(ExpandPid) == 10
            && _playerStore.TryUseExpansionItem(ExpandPid, 0, false, 10, out byte invenStep) && invenStep == 2
            && _playerStore.TryUseExpansionItem(ExpandPid, 1, true, 10, out byte bankStep) && bankStep == 2
            && _playerStore.MainCapacity(ExpandPid) == 78 && _playerStore.BankCapacity(ExpandPid) == 20
            && _playerStore.GetInventory(ExpandPid, () => []).Single(i => i.InventoryIndex == 0 && i.InventoryCell == 1)
                is { ItemId: 675001, Count: 2 },
            "retail expansion items persist MAIN step 1->2 (48->78) and bank step 1->2 (10->20)");
        const int legacyExpandPid = 920097;
        ResetCharacter(legacyExpandPid);
        _playerStore.GetInventory(legacyExpandPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 60, ItemId = 604221, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 5, InventoryCell = 27, ItemId = 604222, Count = 1 }
        ]);
        Check(_playerStore.GetProgress(legacyExpandPid) is { InvenExpandStep: 2, BankExpandStep: 3 }
            && _playerStore.MainCapacity(legacyExpandPid) == 78 && _playerStore.BankCapacity(legacyExpandPid) == 30,
            "legacy maximum-bound inventories infer expansion steps without hiding existing player property");
        Check(_itemCatalog.Get(120689) is { ItemLimitTimeType: 2, ItemLimitDuration: 3 }
            && _itemCatalog.Get(678202) is { ItemLimitTimeType: 4, CityItemDuration: 1440 }
            && _itemCatalog.Get(111201) is { ItemLimitTimeType: 1, ItemLimitDuration: 1440 },
            "exported item catalog preserves equipped/use timer modes and minute durations");
        const int expiryPid = 920071;
        ResetCharacter(expiryPid);
        _playerStore.AddItems(expiryPid, [(120689, 1), (604221, 1)]);
        _playerStore.GetInventory(expiryPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 120689, Count = 1, LimitTime = 99, TuneUpOptions = 3 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 604221, Count = 1, LimitTime = 0 }
        ]);
        var prunedExpiry = _playerStore.PruneExpiredInventory(expiryPid, 100);
        Check(prunedExpiry is [{ ItemId: 120689, TuneUpOptions: 3 }]
            && _playerStore.GetInventory(expiryPid, () => []).Single().ItemId == 604221,
            "elapsed absolute item timers are removed while zero/inactive timers remain");
        Check(_playerStore.GetItems(expiryPid) is [{ item: 604221, num: 1 }],
            "expiration atomically removes aggregate ownership instead of leaving a phantom item count");
    }

    private void BindRankAndArmorEquip()
    {
        ServerPolicy.OpenAllWeaponSlots = false;
        const int expansionPolicyPid = 920990;
        ServerPolicy.MaxExpansionByDefault = true;
        Check(_playerStore.MainCapacity(expansionPolicyPid) == 78 && _playerStore.BankCapacity(expansionPolicyPid) == 40
            && _playerStore.GetProgress(expansionPolicyPid) is { InvenExpandStep: 2, BankExpandStep: 4 },
            "server policy gives every character the fully expanded inventory and bank");
        ServerPolicy.MaxExpansionByDefault = false;
        var level20Flinger = _itemCatalog.Get(120205)!;
        _playerStore.GetProgress(_equipPid).BP = PlayerStore.ComputePlayerLevelupGoalBP(18) + 1;
        var tunedLevelSource = new PlayerStore.GenericItem
            { InventoryIndex = 0, InventoryCell = 5, ItemId = 120205, Durability = 100, TuneUpOptions = 1 << 16 };
        Check(EquipmentRules.GetEquipLevel(level20Flinger, 0) == 20
            && EquipmentRules.GetEquipLevel(level20Flinger, tunedLevelSource.TuneUpOptions) == 19
            && _equipmentRules.ValidateWeapon(_equipPid, 0, 5, tunedLevelSource, level20Flinger) == 22,
            "weapon equip applies the recovered level-tune nibble before license validation");
        var rankedWeapon = _itemCatalog.Get(130316)!;
        _playerStore.GetProgress(_equipPid).BP = PlayerStore.ComputePlayerLevelupGoalBP(29) + 1;
        Check(rankedWeapon.EquipRank == 5
            && _equipmentRules.ValidateWeapon(_equipPid, 0, 5,
                new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 5, ItemId = 130316, Durability = 100 }, rankedWeapon) == 41,
            "weapon equip parses RANK_SOLDIER6 and rejects insufficient persistent rank with native result 41");
        const int bindEquipPid = 920093;
        ResetCharacter(bindEquipPid);
        _playerStore.GetInventory(bindEquipPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 130316, Count = 1, State = 0x20, Durability = 100 }
        ]);
        Check(rankedWeapon.BindOnEquip
            && _playerStore.SwapSlots(bindEquipPid, 3, 0, 0, 4)
            && _playerStore.TryBindEquipped(bindEquipPid, 3, 0, out byte boundState)
            && boundState == 0x21
            && _playerStore.GetInventory(bindEquipPid, () => []).Single().State == 0x21,
            "first equip permanently sets ITEMSTATE_BIND while preserving the bind-reset count nibble");
        const int pickupBindPid = 920094;
        ResetCharacter(pickupBindPid);
        _playerStore.GetInventory(pickupBindPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 604221, Count = 1 }
        ]);
        Check(_itemCatalog.Get(608001) is { BindOnPickup: true }
            && _playerStore.TryAddQuestItems(pickupBindPid, [(608001, 1)], out var pickupBound)
            && pickupBound is [{ State: 0x02 }]
            && _playerStore.GetInventory(pickupBindPid, () => []).Single(i => i.ItemId == 608001).State == 0x02,
            "Binds_PickedUp acquisition persists the distinct ITEMSTATE_BIND_WHEN_PICKUP value 0x02");
        Check(_playerStore.SetRank(_equipPid, 5) && _characterManager.BuildPlayerInfo(_equipPid).CharInfo.Rank == 5,
            "authoritative persistent rank is emitted in the full player-info character block");
        Check(_characterManager.BuildPlayerInfo(_equipPid).UserStatus == PlayerInfo.NormalUserStatus
            && PlayerInfo.NormalUserStatus == 1
            && new AddPlayer().UserStatus == PlayerInfo.NormalUserStatus,
            "self and nearby-player state use exported USERSTATUS_NORMAL value 1");
        const int rosterArmorPid = 920095;
        ResetCharacter(rosterArmorPid);
        _playerStore.GetInventory(rosterArmorPid, () =>
        [
            new PlayerStore.GenericItem
            {
                InventoryIndex = 4, InventoryCell = 2, ItemId = 200299, Count = 1, State = 3,
                LimitTime = 4, TuneUpOptions = 5, Skill = 6, Skill1 = 7, Durability = 88
            }
        ]);
        var rosterArmor = _itemManager.BuildRosterArmorPreview(rosterArmorPid);
        Check(rosterArmor.Length == 160
            && rosterArmor[..64].All(x => x == 0)
            && BitConverter.ToInt32(rosterArmor, 64) == 200299
            && BitConverter.ToInt32(rosterArmor, 68) == 1
            && rosterArmor[72] == 4 && rosterArmor[73] == 2 && rosterArmor[74] == 3
            && BitConverter.ToInt32(rosterArmor, 76) == 4
            && BitConverter.ToInt32(rosterArmor, 80) == 5
            && BitConverter.ToInt32(rosterArmor, 84) == 6
            && BitConverter.ToInt32(rosterArmor, 88) == 7
            && BitConverter.ToInt32(rosterArmor, 92) == 88
            && rosterArmor[96..].All(x => x == 0),
            "character roster emits equipped armor in the native five-slot GenericItem preview");
        var customizedArmorPresence = _characterManager.BuildOtherPlayerArmorSnapshot(rosterArmorPid).Single();
        Check(customizedArmorPresence is { Pid: rosterArmorPid, Part: 2,
                Item: { ItemId: 200299, State: 3, LimitTime: 4, TuneUpOptions: 5,
                    Skill: 6, Skill1: 7, Durability: 88 } }
            && customizedArmorPresence.Serialize()[0] == 80,
            "city armor snapshot preserves every customized field for spawn and live equip replication");
        var starterUpbody = _itemCatalog.Get(200199)!;
        var rewardHelmet = _itemCatalog.Get(200001)!;
        var armorSource = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 6, ItemId = 200199, Durability = 100 };
        Check(starterUpbody.ArmorPart.Equals("POS_UpBody", StringComparison.OrdinalIgnoreCase)
            && _equipmentRules.ValidateArmor(_equipPid, 1, 6, armorSource, starterUpbody) == 0
            && _equipmentRules.ValidateArmor(_equipPid, 0, 6, armorSource, starterUpbody) == 12,
            "armor equip enforces the exported ItemPos-to-equipment-cell mapping");
        Check(rewardHelmet.ArmorPart.Equals("POS_Helmet", StringComparison.OrdinalIgnoreCase)
            && _equipmentRules.ValidateArmor(_equipPid, 0, 6,
                new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 6, ItemId = 200001, Durability = 100 }, rewardHelmet) == 0,
            "cooked armor subclasses recover an omitted class-default ItemPos");
        armorSource.Durability = 0;
        Check(_equipmentRules.ValidateArmor(_equipPid, 1, 6, armorSource, starterUpbody) == 1
            && _equipmentRules.ValidateArmor(_equipPid, 5, 6, armorSource, starterUpbody) == 4,
            "armor equip rejects broken items and out-of-range armor slots before mutation");
        var battleItem = _itemCatalog.Get(510001)!;
        var beltSource = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 7, ItemId = 510001, Durability = 100 };
        Check(_equipmentRules.ValidateBelt(_equipPid, 7, beltSource, battleItem) == 0
            && _equipmentRules.ValidateBelt(_equipPid, 7, armorSource, starterUpbody) == 12
            && _equipmentRules.ValidateBelt(_equipPid, 78, beltSource, battleItem) == 6,
            "belt equip accepts only HuxleyBattleItemData and enforces the retail MAIN bound");
        Check(new ProcLicense { Plus = true, Result = 0, License = 710021 }.Serialize()
                .SequenceEqual(new byte[] { 95, 1, 0, 0x85, 0xD5, 0x0A, 0x00 }),
            "server op95 serializes native bool/result/license event order");
        Check(new ResetLicense { Result = 0, LicensePoints = 7, Licenses = [700020, 710021] }.Serialize()
                .SequenceEqual(new byte[] { 96, 0, 7, 0, 2, 0x74, 0xAE, 0x0A, 0, 0x85, 0xD5, 0x0A, 0 }),
            "server op96 serializes native result/word-LP/byte-count/license-array order");
        Check(new SquadNotify { Pid = 920001, Type = 2, Result = 1 }.Serialize()
                .SequenceEqual(new byte[] { 97, 0xC1, 0x09, 0x0E, 0x00, 2, 1 }),
            "server op97 serializes native PID/type/result squad notification order");
        var simulationChannels = new SCChannelList();
        simulationChannels.Channels.Add(new SimulationChannel(1, 2, 3, 42));
        Check(simulationChannels.Serialize().SequenceEqual(
                new byte[] { 129, 1, 1, 2, 3, 0, 42, 0, 0, 0 }),
            "server op129 serializes count plus aligned 8-byte SCChannelInfo");
        Check(new JoinChannel().Serialize().SequenceEqual(new byte[] { 130, 0, 0xFE })
            && new JoinChannel { Result = 1 }.Serialize().SequenceEqual(new byte[] { 130, 1 }),
            "server op130 serializes retail empty-list success and conditional error forms");
        Check(new CreateRoom { RoomId = 7, Species = 2, FullConnection = 1, AgentToken = 99 }
                .Serialize().SequenceEqual(new byte[] { 131, 0, 7, 2, 1, 0, 99, 0, 0, 0 }),
            "server op131 serializes native bbbbbd create-room callback");
        Check(new ChangeSpecies { Squad = 2 }.Serialize().SequenceEqual(new byte[] { 134, 0, 2 })
            && new ChangeSpeciesBC { Pid = 7, Species = 1, Squad = 2 }.Serialize()
                .SequenceEqual(new byte[] { 138, 1, 7, 0, 0, 0, 2 }),
            "simulation species reply/broadcast match native conditional b+b and bdb layouts");
        var newOwnerWire = new NewOwnerBC
        {
            RoomId = 7, Pid = 9, Name = "Neo", Level = 12,
            PingGrade = 4, FullConnection = 1, PublicAddress = 0x01020304,
            PublicPort = 0x0506, PrivateAddress = 0x0708090A, PrivatePort = 0x0B0C
        }.Serialize();
        Check(newOwnerWire.SequenceEqual(new byte[]
            { 137, 1, 7, 0, 0, 0, 9, 0, 0, 0, 78, 101, 111, 0, 12, 4, 1,
              4, 3, 2, 1, 6, 5, 10, 9, 8, 7, 12, 11 }),
            "simulation owner migration matches native op137 bdd,s,bbb,dwdw layout with room-peer PingType 1, not the owner's species");
        Check(new Ready { Pid = 7 }.Serialize().SequenceEqual(new byte[] { 140, 0, 7, 0, 0, 0 })
            && new ReadyCancelBC { Pid = 7 }.Serialize().SequenceEqual(new byte[] { 141, 7, 0, 0, 0 })
            && new SimulPlayerStateBusy { Pid = 7, State = 3 }.Serialize().SequenceEqual(new byte[] { 142, 7, 0, 0, 0, 3 }),
            "simulation ready/cancel/busy callbacks match native b+d, d, and db layouts");
        Check(new ChangeSquad().Serialize().SequenceEqual(new byte[] { 152, 0 })
            && new ChangeSquadBC { Pid = 7, Squad = 2 }.Serialize()
                .SequenceEqual(new byte[] { 153, 7, 0, 0, 0, 2 }),
            "simulation squad reply/broadcast match native b and db layouts");
    }
}
