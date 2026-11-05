using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void AuctionPacketLayouts()
    {
        var auctionList = new AuctionList { Opcode = 161, PageDirection = 1, CurrentPage = 1, TotalPages = 1 };
        auctionList.Entries.Add(new AuctionStore.AuctionItemInfo { Index = 7, SellerPid = 8, Seller = "A", Price = 10,
            CreatedUtc = DateTime.UtcNow - TimeSpan.FromHours(22),
            Item = new PlayerStore.GenericItem { ItemId = 9, Count = 2, TuneUpOptions = 0x12345678 } });
        Check(auctionList.Serialize().SequenceEqual(new byte[] { 161, 1, 1, 1, 1,
                7, 0, 0, 0, 8, 0, 0, 0, 9, 0, 0, 0, 2, 0, 0, 0, 10, 0, 0, 0,
                2, 65, 0, 0x78, 0x56, 0x34, 0x12, 0 })
            && new AuctionResult { Opcode = 164, Answer = 0, Cell = 3 }.Serialize()
                .SequenceEqual(new byte[] { 164, 0, 3 })
            && new AuctionResult { Opcode = 166, Answer = 0, ItemIndex = 9 }.Serialize()
                .SequenceEqual(new byte[] { 166, 0, 9, 0, 0, 0 })
            && new AuctionNotification { Opcode = 168, Result = 0, ItemId = 9 }.Serialize()
                .SequenceEqual(new byte[] { 168, 0, 9, 0, 0, 0 })
            && new AuctionNotification { Opcode = 168, Result = 8, ItemId = 9 }.Serialize()
                .SequenceEqual(new byte[] { 168, 8 })
            && new AuctionNotification { Opcode = 169, Result = 0, ItemId = 9 }.Serialize()
                .SequenceEqual(new byte[] { 169, 0, 9, 0, 0, 0 })
            && new AuctionCheckTimeout { HasItems = true }.Serialize().SequenceEqual(new byte[] { 170, 1 }),
            "auction list entry and result packets match native field order and conditional branches");
        var auctionTimeoutList = new AuctionList { Opcode = 167, PageDirection = 1, CurrentPage = 0, TotalPages = 1 };
        auctionTimeoutList.Entries.Add(new AuctionStore.AuctionItemInfo { Index = 7, SellerPid = 8, Seller = "A", Price = 10,
            CreatedUtc = DateTime.UtcNow - TimeSpan.FromHours(25),
            Item = new PlayerStore.GenericItem { ItemId = 9, Count = 2, TuneUpOptions = 0x12345678 } });
        Check(auctionTimeoutList.Serialize().SequenceEqual(new byte[] { 167, 1, 0, 1, 1,
                7, 0, 0, 0, 8, 0, 0, 0, 9, 0, 0, 0, 2, 0, 0, 0, 10, 0, 0, 0,
                4, 65, 0 }),
            "auction timeout list uses native short dddddbs entries without active-list trailing fields");
        // Native sub_111C4BB0: Send(146, "bbd", cell, period, price).
        var auctionRegisterReader = new PacketReader(new byte[] { 7, 2, 0x78, 0x56, 0x34, 0x12 });
        Check(AuctionRegisterRequest.TryDeserialize(ref auctionRegisterReader, out var auctionRegister)
            && auctionRegister.MainCell == 7 && auctionRegister.SellPrice == 0x12345678
            && auctionRegister.BidPeriod == 2 && auctionRegisterReader.RemainingLength == 0,
            "auction registration parses the native wire order cell/period/price (bbd)");
        // Native sub_111C73E0: Send(143, "bbbbs", category, page, usable, grade, search).
        using (var auctionListWriter = new PacketWriter())
        {
            auctionListWriter.Write((byte)4); auctionListWriter.Write((byte)6); auctionListWriter.Write(true);
            auctionListWriter.Write((byte)6); auctionListWriter.Write("rifle");
            var auctionListReader = new PacketReader(auctionListWriter.Buffer);
            Check(AuctionListRequest.TryDeserialize(ref auctionListReader, out var auctionListRequest)
                && auctionListRequest.Category == 4 && auctionListRequest.Page == 6
                && auctionListRequest.Usable && auctionListRequest.Grade == 6
                && auctionListRequest.Search == "rifle" && auctionListReader.RemainingLength == 0,
                "auction search parses the native bbbbs wire format (byte page direction)");
        }
        Check(CityManager.ResolvePage(0, 3, 5) == 0
            && CityManager.ResolvePage(1, 3, 5) == 3
            && CityManager.ResolvePage(2, 0, 5) == 4
            && CityManager.ResolvePage(3, 3, 5) == 3
            && CityManager.ResolvePage(4, 3, 5) == 0
            && CityManager.ResolvePage(5, 0, 5) == 0
            && CityManager.ResolvePage(6, 4, 5) == 4
            && CityManager.ResolvePage(6, 0, 0) == 0,
            "pagination implements all native ENUM_PAGE directions with zero-based callback pages");
    }

    private void AuctionEscrowAndFees()
    {
        const int auctionSeller = 920010;
        const int auctionBuyer = 920011;
        ResetCharacter(auctionSeller);
        ResetCharacter(auctionBuyer);
        _playerStore.GetInventory(auctionSeller, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 604221, Count = 2, Durability = 100 }
        ]);
        _playerStore.GetInventory(auctionBuyer, () => []);
        long sellerMoney = _playerStore.GetProgress(auctionSeller).Money;
        long buyerMoney = _playerStore.GetProgress(auctionBuyer).Money;
        var auctionFeeItem = new PlayerStore.GenericItem { ItemId = 604221, Count = 2 };
        int auctionBaseFee = _auctionStore.RegistrationFee(auctionSeller, auctionFeeItem);
        Check(_auctionStore.Register(auctionSeller, "Seller", 4, 100, 1, out _) == AuctionStore.Result.CantRegister
            && _playerStore.GetInventory(auctionSeller, () => []).Any(x => x.InventoryCell == 4 && x.ItemId == 604221)
            && _playerStore.GetProgress(auctionSeller).Money == sellerMoney,
            "auction rejects an unsupported period before moving CR or escrow; active sell UI sends zero");
        Check(_auctionStore.Register(auctionSeller, "Seller", 4, 100, 0, out var listing) == AuctionStore.Result.Success
            && listing != null
            && _playerStore.GetInventory(auctionSeller, () => []).All(x => x.InventoryCell != 4)
            && _playerStore.GetProgress(auctionSeller).Money == sellerMoney - auctionBaseFee,
            "auction registration persistently escrows the exact MAIN item and charges the retail 20% fee");
        byte auctionCategory = checked((byte)_itemCatalog.Get(604221)!.AuctionCategory);
        Check(_auctionStore.Bid(auctionBuyer, listing!.Index, out _, out _,
                (byte)((auctionCategory + 1) % 27)) == AuctionStore.Result.AuctionNotExist
            && _auctionStore.All().Any(x => x.Index == listing.Index)
            && _playerStore.GetProgress(auctionBuyer).Money == buyerMoney,
            "auction bid rejects a category mismatched with the displayed listing without charging CR");
        Check(_auctionStore.Bid(auctionBuyer, listing.Index, out var auctionPurchase, out var soldListing,
                auctionCategory) == AuctionStore.Result.Success
            && auctionPurchase is { ItemId: 604221, Count: 2 }
            && soldListing is { SellerPid: auctionSeller, Item.ItemId: 604221 }
            && _playerStore.GetProgress(auctionSeller).Money == sellerMoney - auctionBaseFee + 100
            && _playerStore.GetProgress(auctionBuyer).Money == buyerMoney - 100
            && _auctionStore.All().All(x => x.Index != listing.Index),
            "auction purchase atomically transfers item, currency, and removes the listing");
        Check(_sqliteStore.Get("auction-sequence.json")?.Contains($"\"LastIndex\":{listing.Index}") == true,
            "sold auction index remains reserved after escrow closes");
        _playerStore.GetInventory(auctionSeller, () => []).Add(
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 604221, Count = 1, Durability = 0 });
        AuctionStore.AuctionItemInfo? stackableListing = null;
        Check(_itemCatalog.Get(604221) is { Repairable: false }
            && _auctionStore.Register(auctionSeller, "Seller", 4, 103, 0, out stackableListing)
                == AuctionStore.Result.Success
            && stackableListing is { Item.ItemId: 604221, Item.Durability: 0 },
            "auction accepts non-repairable stackables whose native durability is zero");
        Check(stackableListing!.Index > listing.Index,
            "a new same-category listing never reuses a sold listing index");
        Check(_auctionStore.Cancel(auctionSeller, stackableListing!.Index, out _) == AuctionStore.Result.Success,
            "auction zero-durability stackable fixture is returned from escrow");
        _playerStore.GetInventory(auctionSeller, () => []).Add(
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 7, ItemId = 100499, Count = 1, Durability = 99 });
        Check(_itemCatalog.Get(100499) is { Repairable: true }
            && _auctionStore.Register(auctionSeller, "Seller", 7, 104, 0, out _) == AuctionStore.Result.NeedRepair
            && _playerStore.GetInventory(auctionSeller, () => []).Any(x => x.InventoryCell == 7 && x.Durability == 99),
            "auction rejects damaged repairable gear and restores it after escrow validation");
        _playerStore.GetInventory(auctionSeller, () => []).Add(
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 9, ItemId = 210001, Count = 1, Durability = 99 });
        Check(_auctionStore.Register(auctionSeller, "Seller", 9, 104, 0, out _) == AuctionStore.Result.NeedRepair,
            "auction rejects damaged inherited helmet armor");
        _playerStore.GetInventory(auctionSeller, () => []).Single(x => x.InventoryCell == 9).Durability = 100;
        Check(_auctionStore.Register(auctionSeller, "Seller", 9, 104, 0, out var helmetListing)
                == AuctionStore.Result.Success
            && helmetListing is { Item.ItemId: 210001, Item.Durability: 100 }
            && _auctionStore.Cancel(auctionSeller, helmetListing.Index, out _) == AuctionStore.Result.Success,
            "auction escrows and returns fully repaired inherited helmet armor");
        _playerStore.GetInventory(auctionSeller, () => []).Add(
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 8, ItemId = 604221, Count = 1,
                State = 0x02, Durability = 0, Skill = 303400 });
        long beforeBoundAuction = _playerStore.GetProgress(auctionSeller).Money;
        Check(_auctionStore.Register(auctionSeller, "Seller", 8, 105, 0, out _) == AuctionStore.Result.BindItem
            && _playerStore.GetProgress(auctionSeller).Money == beforeBoundAuction
            && _playerStore.GetInventory(auctionSeller, () => []).Any(x => x.InventoryCell == 8
                && x.State == 0x02 && x.Skill == 303400),
            "auction rejects the native low-nibble bind state and restores the item and fee atomically");
        _playerStore.GetInventory(auctionSeller, () => []).Single(x => x.InventoryCell == 8).State = 0;
        Check(_auctionStore.Register(auctionSeller, "Seller", 8, 105, 0, out var socketedListing)
                == AuctionStore.Result.Success
            && socketedListing is { Item.Skill: 303400 },
            "auction does not confuse socket-skill payload with an item bind flag");
        Check(_auctionStore.Cancel(auctionSeller, socketedListing!.Index, out _) == AuctionStore.Result.Success,
            "auction socket-payload fixture is returned from escrow");
        _licenseStore.Grant(auctionSeller, 713711);
        Check(_licenseCatalog.Get(713711) is { ClassName: "HuxleyLicenseData_AuctionTalent", DiscountAmount: 10 }
            && _licenseStore.AuctionDiscountPercent(auctionSeller) == 10
            && _auctionStore.RegistrationFee(auctionSeller, auctionFeeItem) == (int)(auctionBaseFee * 0.9),
            "auction talent license applies the exported 10% registration-fee discount");
        var auctionRateCharacter = _characterStore.Create(_accountA, "AuctionRate", 1, new Custom());
        var auctionRateItem = new PlayerStore.GenericItem { ItemId = 604221, Count = 3 };
        ResetCharacter(auctionRateCharacter.Pid);
        Check(_economyRates.TrySetAuthoritative(_testRates with { SapiBuy = 1.23f })
            && _auctionStore.RegistrationFee(auctionRateCharacter.Pid, auctionRateItem) == 94
            && _licenseStore.Grant(auctionRateCharacter.Pid, 713711)
            && _auctionStore.RegistrationFee(auctionRateCharacter.Pid, auctionRateItem) == 84,
            "auction fee truncates faction-adjusted price, then listing fee, then license discount");
        _playerStore.GetProgress(auctionRateCharacter.Pid).Money = 1_000;
        _playerStore.GetInventory(auctionRateCharacter.Pid, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = 0, InventoryCell = 2, ItemId = 604221, Count = 3 });
        Check(_auctionStore.Register(auctionRateCharacter.Pid, auctionRateCharacter.Name,
                2, 200, 0, out var auctionRateListing) == AuctionStore.Result.Success
            && auctionRateListing != null
            && _playerStore.GetProgress(auctionRateCharacter.Pid).Money == 916
            && _auctionStore.Cancel(auctionRateCharacter.Pid, auctionRateListing!.Index, out var auctionRateReturn)
                == AuctionStore.Result.Success
            && auctionRateReturn?.Count == 3,
            "auction registration charges the adjusted fee and cancellation returns the escrowed stack");
        _economyRates.ResetNeutral();
        ResetCharacter(auctionSeller);
        _playerStore.GetInventory(auctionSeller, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 5, ItemId = 604221, Count = 1, Durability = 100 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 6, ItemId = 604222, Count = 1, Durability = 100 }
        ]);
        DateTime auctionNow = DateTime.UtcNow;
        var activeRegister = _auctionStore.Register(auctionSeller, "Seller", 5, 101, 0, out var activeListing,
            auctionNow - TimeSpan.FromHours(1));
        var expiredRegister = _auctionStore.Register(auctionSeller, "Seller", 6, 102, 0, out var expiredListing,
            auctionNow - TimeSpan.FromHours(24));
        Check(activeRegister == AuctionStore.Result.Success
            && expiredRegister == AuctionStore.Result.Success
            && activeListing != null && expiredListing != null,
            "auction fixtures persist active and exactly-24-hour timeout listings");
        Check(AuctionStore.RemainTime(activeListing!, auctionNow) == 0
            && AuctionStore.RemainTime(new AuctionStore.AuctionItemInfo { CreatedUtc = auctionNow - TimeSpan.FromHours(12) }, auctionNow) == 1
            && AuctionStore.RemainTime(new AuctionStore.AuctionItemInfo { CreatedUtc = auctionNow - TimeSpan.FromHours(22) }, auctionNow) == 2
            && AuctionStore.RemainTime(new AuctionStore.AuctionItemInfo { CreatedUtc = auctionNow - TimeSpan.FromHours(23.5) }, auctionNow) == 3
            && AuctionStore.RemainTime(expiredListing!, auctionNow) == 4,
            "auction remaining-time values match the exported 12h/2h/30m/deadline enum");
        Check(_auctionStore.Active(auctionNow).Any(x => x.Index == activeListing!.Index)
            && _auctionStore.Active(auctionNow).All(x => x.Index != expiredListing!.Index)
            && _auctionStore.TimedOut(auctionSeller, auctionNow).Any(x => x.Index == expiredListing!.Index)
            && _auctionStore.HasTimedOut(auctionSeller, auctionNow)
            && _auctionStore.Bid(auctionBuyer, expiredListing!.Index, out _) == AuctionStore.Result.TimeoutDeleted,
            "auction searches exclude timed-out escrow and expired bids return AUCTION_TIMEOUT_DELETED");
        var timeoutReturns = _auctionStore.ReturnTimedOutDetailed(auctionSeller, auctionNow);
        Check(timeoutReturns is [{ ItemId: 604222, Item.ItemId: 604222 }]
            && _auctionStore.All().Any(x => x.Index == activeListing!.Index)
            && _auctionStore.All().All(x => x.Index != expiredListing!.Index),
            "auction return-all retrieves only timed-out escrow without cancelling active listings");
        const int weaponSeller = 920070;
        ResetCharacter(weaponSeller);
        _playerStore.GetInventory(weaponSeller, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 128403, Count = 1, Durability = 100 }
        ]);
        Check(_auctionStore.Register(weaponSeller, "WeaponSeller", 0, 5000, 0, out var weaponListing)
                == AuctionStore.Result.Success && weaponListing != null,
            "auction category fixture escrows an exported machine gun");
        int weaponListingIndex = weaponListing!.Index;
        Check(_auctionStore.Search(auctionBuyer, 3, false, 8, null).Any(x => x.Index == weaponListingIndex)
            && _auctionStore.Search(auctionBuyer, 26, false, 8, null).All(x => x.Index != weaponListingIndex)
            && _auctionStore.Search(auctionBuyer, 3, false, 4, null).Any(x => x.Index == weaponListingIndex)
            && _auctionStore.Search(auctionBuyer, 3, false, 1, null).All(x => x.Index != weaponListingIndex),
            "auction search enforces native category and grade filters while Grade_None means any grade");
        Check(_auctionStore.Search(auctionBuyer, 3, true, 8, null).All(x => x.Index != weaponListingIndex)
            && _auctionStore.Search(auctionBuyer, 3, false, 8, "WeaponSeller").Any(x => x.Index == weaponListingIndex)
            && _auctionStore.Search(auctionBuyer, 3, false, 8, "Rapier").Any(x => x.Index == weaponListingIndex),
            "auction usable filter rejects gear failing authoritative equip qualifications and text search matches seller or localized item name");
        Check(_auctionStore.Search(auctionBuyer, 26, true, 8, null).Any(x => x.Item.ItemId == 604221),
            "auction usable filter retains ordinary items that satisfy their level and rank gates");
        Check(_auctionStore.HasListings(weaponSeller),
            "character deletion can detect auction escrow before permanent teardown");
    }
}
