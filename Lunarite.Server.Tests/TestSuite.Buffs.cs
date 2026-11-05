using System.Buffers.Binary;
using System.Net;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void EntitlementCallbacks()
    {
        const int expirationClock = 1_700_000_000;
        var durativeClockWire = new UpdateListOfDurativeItem { UnixSeconds = expirationClock }.Serialize();
        var buffClockWire = new UpdateListOfPersonalBuffs { UnixSeconds = expirationClock }.Serialize();
        Check(durativeClockWire[0] == 230 && buffClockWire[0] == 232
            && BinaryPrimitives.ReadInt32LittleEndian(durativeClockWire.AsSpan(1)) == expirationClock
            && BinaryPrimitives.ReadInt32LittleEndian(buffClockWire.AsSpan(1)) == expirationClock,
            "durative-item and personal-buff expiration callbacks carry authoritative Unix seconds");
        var durativeListWire = new DurativeItemList
        {
            Items = [new() { ItemID = 11, OptionValue = 22, ExpirationDate = 33, AddInfo = 44, CityItemGroup = 5 }]
        }.Serialize();
        var buffListWire = new PersonalBuffs
        {
            Buffs = [new() { ID = 66, Group = 1, ModifierType = 2, CanBeCumulated = 1, Level = 3, ExpirationDate = 77 }]
        }.Serialize();
        Check(durativeListWire[0] == 231 && BinaryPrimitives.ReadInt32LittleEndian(durativeListWire.AsSpan(1)) == 1
            && durativeListWire.Length == 25 && buffListWire[0] == 233 && buffListWire[1] == 1
            && buffListWire.Length == 14,
            "durative-item and personal-buff full-list callbacks use native op231/op233 layouts");
        Check(new MedalParameterUpdate { Success = false }.Serialize().SequenceEqual(new byte[] { 208, 0 })
            && new MedalParameterUpdate { Success = true, MedalId = 609001, Count = 7 }.Serialize()
                .SequenceEqual(new byte[] { 208, 1, 233, 74, 9, 0, 7, 0, 0, 0 }),
            "city medal parameter updates use native op208 failure and success branches");
        var styleWire = new SelectStyle
        {
            SkillSlots = [4, 3, 2, 1, 0], Licenses = [101, 202],
            Items = [new(3, Enumerable.Range(0, 32).Select(x => (byte)x).ToArray())]
        }.Serialize();
        Check(styleWire[0] == 216
            && styleWire.AsSpan(1, 5).SequenceEqual(new byte[] { 4, 3, 2, 1, 0 })
            && styleWire[6] == 2
            && BinaryPrimitives.ReadInt32LittleEndian(styleWire.AsSpan(7)) == 101
            && BinaryPrimitives.ReadInt32LittleEndian(styleWire.AsSpan(11)) == 202
            && styleWire.AsSpan(15, 7).SequenceEqual(new byte[] { 0, 0, 0, 1, 0, 0, 0 })
            && styleWire.Length == 54,
            "style selection emits native op216 skill, license, inventory-count, and item stream order");
    }

    private void WeaponSlotEntitlement()
    {
        _durativeItemStore.Reset(_equipPid);
        Check(!_equipmentRules.IsOpenedWeaponSlot(_equipPid, 2)
            && _equipmentRules.ValidateWeapon(_equipPid, 2, 4, _equipSource, _equipFlinger) == 5
            && _durativeItemStore.TryActivate(_equipPid, _itemCatalog.Get(676001)!, out var slotTwoPass)
            && slotTwoPass is { CityItemGroup: 9, OptionValue: 2 }
            && _equipmentRules.IsOpenedWeaponSlot(_equipPid, 2)
            && !_equipmentRules.IsOpenedWeaponSlot(_equipPid, 3)
            && _equipmentRules.ValidateWeapon(_equipPid, 2, 4, _equipSource, _equipFlinger) == 0,
            "weapon slots 2 and 3 follow the active table-backed OpenWeaponSlot entitlement");
        _durativeItemStore.Reset(_equipPid);
        ServerPolicy.OpenAllWeaponSlots = true;
        var policySlots = _durativeItemStore.Active(_equipPid);
        Check(_equipmentRules.IsOpenedWeaponSlot(_equipPid, 2) && _equipmentRules.IsOpenedWeaponSlot(_equipPid, 3)
            && policySlots.Count == 2
            && policySlots.All(x => x is { ItemID: 0, ExpirationDate: 0, CityItemGroup: 9 })
            && policySlots.Select(x => x.OptionValue).SequenceEqual([2, 3])
            && _equipmentRules.ValidateWeapon(_equipPid, 3, 4, _equipSource, _equipFlinger) == 0,
            "server policy opens weapon slots 3 and 4 for everyone through permanent ItemID-0 durative entries");
    }

    // The once-a-second expiry sweep sends op230/op232 only when something lapsed. A client
    // loading a map crashes on them, so an idle player must hear nothing.
    private void ExpirySweepStaysQuiet()
    {
        const int sweepPid = 954500777;
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peer = new System.Net.Sockets.TcpClient();
        peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var accepted = listener.AcceptTcpClient();
        listener.Stop();
        using var client = new Client(954500777, new Server(0, HandlerType.Battle, static (_, _) => { }, _loggerFactory), accepted);
        var stream = peer.GetStream();
        peer.ReceiveTimeout = 400;
        bool Quiet()
        {
            try { stream.ReadExactly(new byte[2]); return false; }
            catch (IOException) { return true; }
        }
        _personalBuffStore.Reset(sweepPid);
        _playerManager.SetOnline(sweepPid, client);
        try
        {
            _itemManager.SweepExpiredItems();
            bool idleQuiet = Quiet();
            _personalBuffStore.Put(sweepPid, new PersonalBuff { ID = 2, Group = 1, ModifierType = 1, CanBeCumulated = 1, Level = 1, ExpirationDate = 1 });
            _itemManager.SweepExpiredItems();
            peer.ReceiveTimeout = 3000;
            Check(idleQuiet && !Quiet(),
                "the expiry sweep sends nothing to an idle player and still reports a lapsed buff");
        }
        finally
        {
            _playerManager.SetOffline(sweepPid);
            _personalBuffStore.Reset(sweepPid);
        }
    }

    private void PersonalBuffsAndCashShop()
    {
        var testBuff = new PersonalBuff { ID = 2, Group = 1, ModifierType = 1, CanBeCumulated = 1, Level = 3, ExpirationDate = int.MaxValue };
        Check(new PersonalBuffResult { Action = 6, Buff = testBuff }.Serialize()
            .SequenceEqual(new byte[] { 234, 6, 2, 0, 0, 0, 1, 1, 1, 3, 0xFF, 0xFF, 0xFF, 0x7F }),
            "server op234 serializes action plus exact 12-byte PersonalBuff");
        Check(new PersonalBuffResult { Action = 2 }.Serialize().SequenceEqual(new byte[] { 234, 2 })
            && new BuyDrink { Result = 1 }.Serialize().SequenceEqual(new byte[] { 237, 1 }),
            "personal-buff error and drink-result branches match native one-byte layouts");
        var retailBuff = _personalBuffCatalog.GetBuff(2);
        var retailSetter = _personalBuffCatalog.GetSetter(1);
        Check(retailBuff is { Group: 1, ModifierType: 1, CanBeCumulated: 1, DurationSeconds: 120 }
            && retailSetter is { Price: 0, GaugeSpeed: 5, SideEffectProbability: 70 }
            && retailSetter.Great == new PersonalBuffCatalog.Choice(1, 10, 3)
            && retailSetter.Handicap == new PersonalBuffCatalog.Choice(2, 35, 5),
            "personal-buff catalog loads retail buff and setter defaults from the exported table");
        Check(_personalBuffCatalog.GetSetter(3)?.SideEffect == new PersonalBuffCatalog.Choice(3, 10, 1),
            "personal-buff setter omitted goal ratio and max level retain native struct defaults");
        const int drinkChargePid = 954500321;
        ResetCharacter(drinkChargePid);
        _playerStore.GetProgress(drinkChargePid).Money = 25;
        Check(_itemManager.TryChargeDrink(drinkChargePid, 0) == 0
            && _itemManager.TryChargeDrink(drinkChargePid, -1) == 1
            && _itemManager.TryChargeDrink(drinkChargePid, 26) == 2
            && _playerStore.GetProgress(drinkChargePid).Money == 25
            && _itemManager.TryChargeDrink(drinkChargePid, 25) == 0
            && _playerStore.GetProgress(drinkChargePid).Money == 0,
            "pub drink purchase preserves free setters and charges paid setters once with native results");
        Check(new PayItem { Subtype = 0, Result = 7, Extra = new byte[4] }.Serialize()
                .SequenceEqual(new byte[] { 227, 0, 7, 0, 0, 0, 0 })
            && new PayItem { Subtype = 17, Result = 33 }.Serialize()
                .SequenceEqual(new byte[] { 227, 17, 33 }),
            "server op227 cash-info and shop-open branches match the native multiplexed layout");
        using (var giftSent = new PacketWriter())
        using (var giftReceived = new PacketWriter())
        {
            giftSent.Write(0); giftSent.Write(0); giftReceived.Write("AccountA");
            Check(new PayItem { Subtype = 2, Result = 28, Extra = giftSent.Buffer }.Serialize()
                    .SequenceEqual(new byte[] { 227, 2, 28, 0, 0, 0, 0, 0, 0, 0, 0 })
                && new PayItem { Subtype = 2, Result = 29, Extra = giftReceived.Buffer }.Serialize()
                    .SequenceEqual(new byte[] { 227, 2, 29, (byte)'A', (byte)'c', (byte)'c', (byte)'o',
                        (byte)'u', (byte)'n', (byte)'t', (byte)'A', 0 }),
                "cash-shop gifting uses native sender-success and receiver-notification op227 layouts");
        }
        _personalBuffStore.Put(920013, testBuff);
        Check(_personalBuffStore.Active(920013).Single().Level == 3
            && _personalBuffStore.TryDelete(920013, 2, out var deletedBuff) && deletedBuff.ModifierType == 1
            && _personalBuffStore.Active(920013).Count == 0,
            "personal buffs persist and delete by retail buff ID");
        _personalBuffStore.Put(920013, new PersonalBuff { ID = 1, ExpirationDate = 2_000_000_000 });
        _personalBuffStore.Put(920013, new PersonalBuff { ID = 2, ExpirationDate = 2_000_000_001 });
        Check(_personalBuffStore.Active(920013, 2_000_000_000) is [{ ID: 2 }]
            && _personalBuffStore.PruneExpired(920013, 2_000_000_000) is [{ ID: 1 }]
            && _personalBuffStore.Active(920013, 2_000_000_000) is [{ ID: 2 }],
            "personal-buff reads hide expiration without consuming the online sweep transition");
        _personalBuffStore.Reset(920013);
        var cumulativeBuff = new PersonalBuff { ID = 2, Group = 1, ModifierType = 1, CanBeCumulated = 1, Level = 1, ExpirationDate = int.MaxValue };
        Check(_personalBuffStore.Apply(920014, cumulativeBuff, 2, out var addedBuff) == 5 && addedBuff.Level == 1
            && _personalBuffStore.Apply(920014, cumulativeBuff, 2, out var incrementedBuff) == 7 && incrementedBuff.Level == 2
            && _personalBuffStore.Apply(920014, cumulativeBuff, 2, out _) == 4,
            "retail cumulative personal buffs add, increment, then enforce setter max level");
        _personalBuffStore.Reset(920016);
        _personalBuffStore.Put(920016, new PersonalBuff
        {
            ID = 10, Group = 2, ModifierType = 1, Level = 3, ExpirationDate = int.MaxValue
        });
        var weakerGroupedBuff = new PersonalBuff
        {
            ID = 11, Group = 2, ModifierType = 2, Level = 2, ExpirationDate = int.MaxValue
        };
        var strongerGroupedBuff = new PersonalBuff
        {
            ID = 12, Group = 2, ModifierType = 3, Level = 4, ExpirationDate = int.MaxValue
        };
        Check(_personalBuffStore.Apply(920016, weakerGroupedBuff, 4, out var retainedGroupedBuff) == 3
            && retainedGroupedBuff.ID == 10
            && _personalBuffStore.Active(920016) is [{ ID: 10, Level: 3 }],
            "weaker same-group personal buff returns EXIST_HIGHLEVEL without replacing active state");
        Check(_personalBuffStore.Apply(920016, strongerGroupedBuff, 4, out var upgradedGroupedBuff) == 5
            && upgradedGroupedBuff.ID == 12
            && _personalBuffStore.Active(920016) is [{ ID: 12, Level: 4 }],
            "stronger same-group personal buff replaces the weaker active buff");
        _personalBuffStore.Put(920017, new PersonalBuff
        {
            ID = 1, Group = 1, ModifierType = 3, Level = 4, ExpirationDate = 1
        });
        var replacementBuff = new PersonalBuff
        {
            ID = 1, Group = 1, ModifierType = 3, Level = 1,
            ExpirationDate = checked((int)DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds())
        };
        Check(_personalBuffStore.Apply(920017, replacementBuff, 4, out var replacedExpiredBuff) == 5
            && replacedExpiredBuff.Level == 1
            && _personalBuffStore.Active(920017) is [{ ID: 1, Level: 1 }],
            "expired personal buffs are pruned before a new pub result is applied");
        _playerStore.AddPersonalRespawns(920015, 2);
        Check(_playerStore.GetProgress(920015).RespawnCount == 2
            && _playerStore.TryUsePersonalRespawn(920015)
            && _playerStore.GetProgress(920015).RespawnCount == 1,
            "personal respawn count persists and is consumed atomically");
        Check(new Parameter
            {
                Param = Parameter.PARAMETER_RESPAWNCOUNT,
                Value = _playerStore.GetProgress(920015).RespawnCount
            }.Serialize().SequenceEqual(new byte[] { 16, 16, 1, 0, 0, 0 }),
            "personal-respawn item refresh uses native PARAMETER_RESPAWNCOUNT");
        _playerStore.GetInventory(920016, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 609001, Count = 4 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 2, ItemId = 609101, Count = 3 }
        ]);
        _playerStore.GetProgress(920016).BP = PlayerStore.ComputePlayerLevelupGoalBP(29) + 1;
        Check(_playerStore.TryDonateCityMedals(920016, 609001, 2, 609101, 1)
            && _playerStore.GetProgress(920016).AMedalsDonated == 2
            && _playerStore.GetProgress(920016).BMedalsDonated == 1
            && _playerStore.GetInventory(920016, () => []).Single(x => x.ItemId == 609001).Count == 2
            && !_playerStore.TryDonateCityMedals(920016, 609001, 99, 609101, 0),
            "city medal donation atomically consumes exact retail medal IDs and persists totals");
        var persistedPlayerInfo = _characterManager.BuildPlayerInfo(920016);
        Check(persistedPlayerInfo.Medals[0].MedalID == 609001 && persistedPlayerInfo.Medals[0].MedalCount == 2
            && persistedPlayerInfo.Medals[1].MedalID == 609101 && persistedPlayerInfo.Medals[1].MedalCount == 1,
            "character selection PlayerInfo restores persisted medal totals instead of fabricated defaults");
        _durativeItemStore.ReplaceAll(920016,
        [
            new DurativeItem { ItemID = 650001, OptionValue = 2, ExpirationDate = int.MaxValue, AddInfo = 9, CityItemGroup = 3 },
            new DurativeItem { ItemID = 650002, OptionValue = 4, ExpirationDate = 1000, AddInfo = 8, CityItemGroup = 5 }
        ]);
        var activeDurative = _durativeItemStore.Active(920016, 1500);
        Check(activeDurative is [{ ItemID: 650001, OptionValue: 2, ExpirationDate: int.MaxValue, AddInfo: 9, CityItemGroup: 3 }]
            && _characterManager.BuildPlayerInfo(920016).DurativeItems is [{ ItemID: 650001 }],
            "durative entitlements persist, preserve the native record, hide expiry, and restore in PlayerInfo");
        Check(_durativeItemStore.PruneExpired(920016, 1500) is [{ ItemID: 650002 }],
            "entitlement reads retain expired records for the online notification sweep");
        _durativeItemStore.ReplaceAll(920016,
        [
            new DurativeItem { ItemID = 650003, ExpirationDate = 2000, CityItemGroup = 5 },
            new DurativeItem { ItemID = 650004, ExpirationDate = 2001, CityItemGroup = 5 },
            new DurativeItem { ItemID = 650005, ExpirationDate = 0, CityItemGroup = 5 }
        ]);
        Check(_durativeItemStore.PruneExpired(920016, 2000) is [{ ItemID: 650003 }]
            && _durativeItemStore.Active(920016, 2000).Select(x => x.ItemID).SequenceEqual([650004, 650005]),
            "online durative sweep expires the exact server-time boundary and preserves permanent records");
        _durativeItemStore.Reset(920016);
        var termLicense = _itemCatalog.Get(670101)!;
        Check(termLicense is { CityItemGroup: 5, CityItemDuration: 5, GeneralValue: 703302, CanBeOverlapped: true },
            "term-license metadata is parsed from the retail UE3 table");
        _durativeItemStore.Reset(920017);
        Check(_durativeItemStore.TryActivate(920017, termLicense, out var activatedTerm, 1000)
            && activatedTerm is { ItemID: 670101, OptionValue: 703302, ExpirationDate: 1300, AddInfo: 0, CityItemGroup: 5 }
            && !_durativeItemStore.TryActivate(920017, termLicense, out _, 1001)
            && _durativeItemStore.TryActivate(920017, _itemCatalog.Get(670102)!, out var secondTerm, 1001)
            && secondTerm is { OptionValue: 703303, ExpirationDate: 1301 }
            && _durativeItemStore.Active(920017, 1300) is [{ ItemID: 670102 }],
            "durative activation applies exact minute duration, same-item rejection, option overlap, and expiry filtering");
        _durativeItemStore.Reset(920018);
        Check(_durativeItemStore.TryActivate(920018, termLicense, out _, 1000)
            && _durativeItemStore.TryActivate(920018, termLicense, out _, 1300)
            && _durativeItemStore.PruneExpired(920018, 1300) is [{ ItemID: 670101, OptionValue: 703302 }]
            && _durativeItemStore.Active(920018, 1300) is [{ ItemID: 670101, OptionValue: 703302 }],
            "reactivation after expiry retains the old license grant for live revocation");
        _durativeItemStore.Reset(920018);
        _licenseStore.Reset(920017);
        Check(_licenseStore.Grant(920017, 700020)
            && _licenseStore.Owned(920017).SequenceEqual(new[] { 700020 })
            && _licenseStore.Effective(920017, 1300).SequenceEqual(new[] { 700020, 703303 })
            && _licenseStore.Effective(920017, 1301).SequenceEqual(new[] { 700020 })
            && _licenseStore.Owned(920017).SequenceEqual(new[] { 700020 }),
            "active term-license targets join the effective client license list and expire without changing permanent ownership");
        var expiredLicense = new DurativeItem { ItemID = 670101, OptionValue = 703302, CityItemGroup = 5 };
        var expiredLicenseEvents = CharacterManager.BuildExpiredTemporaryLicenseResults(
            [expiredLicense, expiredLicense], [700020]);
        Check(expiredLicenseEvents.Count == 1
            && expiredLicenseEvents[0].Serialize().SequenceEqual(new byte[] { 95, 0, 0, 0x46, 0xBB, 0x0A, 0 })
            && CharacterManager.BuildExpiredTemporaryLicenseResults([expiredLicense], [703302]).Count == 0,
            "term-license expiry removes the live client license once but preserves any surviving grant");
        _licenseStore.Reset(920017);
        _durativeItemStore.ReplaceAll(920017,
        [
            new DurativeItem { ItemID = 670101, OptionValue = 703302,
                ExpirationDate = int.MaxValue, CityItemGroup = 5 }
        ]);
        Check(_licenseStore.TryAcquire(920017, 703302) == LicenseStore.AcquireResult.AlreadyHave
            && _licenseStore.Owned(920017).Length == 0
            && !_licenseStore.HasPermanentLicenses(920017),
            "active term licenses satisfy the retail HaveLicense ownership check without becoming permanent");
        Check(_licenseStore.Grant(920017, 700020) && _licenseStore.HasPermanentLicenses(920017)
            && _licenseStore.ClearAndRefund(920017) == _licenseCatalog.Get(700020)!.LicensePoint
            && !_licenseStore.HasPermanentLicenses(920017)
            && _licenseStore.Effective(920017).SequenceEqual(new[] { 703302 }),
            "license reset eligibility and refunds cover permanent LP purchases without clearing term entitlements");
        _durativeItemStore.Reset(920017);
        var contamination = _itemCatalog.Get(678001)!;
        Check(contamination is { CityItemGroup: 11, CityItemDuration: 1440, GeneralValue: 3, CanBeOverlapped: false }
            && _durativeItemStore.TryActivate(920017, contamination, out _, 1400)
            && !_durativeItemStore.TryActivate(920017, _itemCatalog.Get(678002)!, out _, 1401)
            && _durativeItemStore.TryActivate(920017, _itemCatalog.Get(678002)!, out var replacedContamination, 1401,
                replaceExistingGroup: true)
            && replacedContamination is { ItemID: 678002, OptionValue: 5, ExpirationDate: 87801 }
            && _durativeItemStore.Active(920017, 1401).Count(x => x.CityItemGroup == 11) == 1,
            "contamination duration items replace their existing group as required by the exported use dialog");
        _durativeItemStore.Reset(920019);
        bool originalEffectActivated = _durativeItemStore.TryActivate(920019, contamination,
            out var originalContamination, 1400);
        bool replacementEffectActivated = _durativeItemStore.TryActivate(920019, _itemCatalog.Get(678002)!,
            out var unconsumedActivation, out var displacedContamination, 1401, replaceExistingGroup: true);
        Check(originalEffectActivated && replacementEffectActivated
            && displacedContamination is [{ ItemID: 678001 }],
            "replacement activation retains the displaced entitlement until item consumption commits");
        _durativeItemStore.RollBackActivation(920019, unconsumedActivation!, displacedContamination);
        Check(_durativeItemStore.Active(920019, 1401) is [{ ItemID: 678001 }]
            && _durativeItemStore.Active(920019, 1401)[0].ExpirationDate == originalContamination!.ExpirationDate,
            "failed item consumption restores the previous contamination effect instead of deleting it");
        _durativeItemStore.Reset(920019);
        _durativeItemStore.Reset(920017);
        Check(new QuestProgressAnswer { Opcode = 58, Answer = 0, QuestIndex = 820102,
                Index = 2, Count = 7, State = 1 }.Serialize()
            .SequenceEqual(new byte[] { 58, 0, 0x86, 0x83, 0x0C, 0, 2, 7, 1 }),
            "server op58 closes the native objective transaction with exact bdbbb order");
    }

    private void MissionPostDataEntitlements()
    {
        byte[] expectedBuff = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(expectedBuff, 7654321);
        expectedBuff[4] = 2; expectedBuff[5] = 3; expectedBuff[6] = 1; expectedBuff[7] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(expectedBuff.AsSpan(8), 123456789);
        Check(_questCreateAppearance.AsSpan().IndexOf(expectedBuff) >= 0
            && _questCreateAppearance.AsSpan().IndexOf(BitConverter.GetBytes(710021)) >= 0,
            "op42 embeds native 12-byte personal-buff and int license arrays for mission PostData");
        byte[] expectedDurative = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(expectedDurative, 650001);
        BinaryPrimitives.WriteInt32LittleEndian(expectedDurative.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(expectedDurative.AsSpan(8), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(expectedDurative.AsSpan(12), 9);
        expectedDurative[16] = 3;
        Check(_questCreateAppearance.AsSpan().IndexOf(expectedDurative) >= 0,
            "op42 embeds the persisted native 20-byte durative-entitlement record");
        int travelItemOffset = _questCreateAppearance.Length - 64;
        Check(_questCreateAppearance[travelItemOffset - 7 + 4] == 1
            && _questCreateAppearance[travelItemOffset - 7 + 6] == 1
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(travelItemOffset)) == 200499
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(travelItemOffset + 4)) == 200499
            && _questCreateAppearance[travelItemOffset + 8] == 4
            && _questCreateAppearance[travelItemOffset + 9] == 4
            && _questCreateAppearance[travelItemOffset + 12] == 4
            && _questCreateAppearance[travelItemOffset + 13] == 4
            && _questCreateAppearance[travelItemOffset + 14] == 2
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(travelItemOffset + 20)) == 8
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(travelItemOffset + 24)) == 303400
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(travelItemOffset + 28)) == 9
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(travelItemOffset + 32)) == 100
            && BinaryPrimitives.ReadInt32LittleEndian(_questCreateAppearance.AsSpan(travelItemOffset + 36)) == 0
            && _questCreateAppearance[travelItemOffset + 40] == 6
            && _questCreateAppearance[travelItemOffset + 41] == 0,
            "op42 P2P travel inventory aligns socket skill and inventory-6 terminal durability for displaced arData");

        var retailQuestCreate = new QuestCreate
        {
            Answer = 0, Type = 1, HostMap = "Q-SP_Begin", PlayerBP = 123,
            Items = [new QuestCreate.Item
            {
                ItemID = 200499, Count = 1, InvenIndex = 4, Cell = 4, State = 2,
                LimitTime = 7, TuneUpOptions = 8, Skill0 = 303400, Skill1 = 9, Durability = 100
            }]
        }.Serialize();
        var patchedP2PQuestCreate = new QuestCreate
        {
            Answer = 0, Type = 4, HostMap = "Q-SP_Begin", PlayerBP = 123,
            UseNativeP2PBackup = true,
            Items = [new QuestCreate.Item
            {
                ItemID = 200499, Count = 1, InvenIndex = 4, Cell = 4, State = 2,
                LimitTime = 7, TuneUpOptions = 8, Skill0 = 303400, Skill1 = 9, Durability = 100
            }]
        }.Serialize();
        int patchedP2PItemOffset = patchedP2PQuestCreate.Length - 32;
        Check(patchedP2PQuestCreate[patchedP2PItemOffset - 11] == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(patchedP2PQuestCreate.AsSpan(patchedP2PItemOffset - 10)) == 0
            && patchedP2PQuestCreate[patchedP2PItemOffset - 8] == 0
            && patchedP2PQuestCreate[patchedP2PItemOffset - 7 + 4] == 1
            && BinaryPrimitives.ReadInt32LittleEndian(patchedP2PQuestCreate.AsSpan(patchedP2PItemOffset)) == 200499
            && BinaryPrimitives.ReadInt32LittleEndian(patchedP2PQuestCreate.AsSpan(patchedP2PItemOffset + 4)) == 1
            && patchedP2PQuestCreate[patchedP2PItemOffset + 8] == 4
            && patchedP2PQuestCreate[patchedP2PItemOffset + 9] == 4
            && patchedP2PQuestCreate[patchedP2PItemOffset + 10] == 2
            && BinaryPrimitives.ReadInt32LittleEndian(patchedP2PQuestCreate.AsSpan(patchedP2PItemOffset + 20)) == 303400
            && BinaryPrimitives.ReadInt32LittleEndian(patchedP2PQuestCreate.AsSpan(patchedP2PItemOffset + 28)) == 100,
            "opt-in Type-4 op42 carries native quest backup prefix and undisplaced GenericItem");
        int retailItemOffset = retailQuestCreate.Length - 32;
        Check(retailQuestCreate[retailItemOffset - 11] == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(retailQuestCreate.AsSpan(retailItemOffset - 10)) == 0
            && retailQuestCreate[retailItemOffset - 8] == 0
            && BinaryPrimitives.ReadInt32LittleEndian(retailQuestCreate.AsSpan(retailItemOffset)) == 200499
            && BinaryPrimitives.ReadInt32LittleEndian(retailQuestCreate.AsSpan(retailItemOffset + 4)) == 1
            && retailQuestCreate[retailItemOffset + 8] == 4
            && retailQuestCreate[retailItemOffset + 9] == 4
            && retailQuestCreate[retailItemOffset + 10] == 2
            && BinaryPrimitives.ReadInt32LittleEndian(retailQuestCreate.AsSpan(retailItemOffset + 20)) == 303400
            && BinaryPrimitives.ReadInt32LittleEndian(retailQuestCreate.AsSpan(retailItemOffset + 28)) == 100,
            "non-P2P op42 carries native quest backup prefix and exact 32-byte GenericItem");
    }
}
