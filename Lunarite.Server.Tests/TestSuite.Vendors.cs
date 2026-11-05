using System.Buffers.Binary;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void VendorBuyAndMailRouting()
    {
        long vendorGateMoney = _playerStore.GetProgress(VendorGatePid).Money;
        byte[] unlistedVendorBuy = new byte[10];
        unlistedVendorBuy[0] = 64;
        BinaryPrimitives.WriteInt32LittleEndian(unlistedVendorBuy.AsSpan(1), 604221);
        unlistedVendorBuy[5] = 0xFF;
        BinaryPrimitives.WriteInt32LittleEndian(unlistedVendorBuy.AsSpan(6), 1);
        Game.OnHandlePacket(_questSocketClient, unlistedVendorBuy);
        Check(ReadQuestSocketPacket().SequenceEqual(new BuyItem { Result = 7 }.Serialize())
            && _playerStore.GetProgress(VendorGatePid).Money == vendorGateMoney
            && _playerStore.GetInventory(VendorGatePid, () => []).Count == 0,
            "actual Game buy packet rejects an item outside cooked vendor groups without mutation");
        _playerStore.AddMoney(VendorGatePid, 1_000_000);
        byte[] listedVendorBuy = (byte[])unlistedVendorBuy.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(listedVendorBuy.AsSpan(1), 100101);
        long listedBuyMoney = _playerStore.GetProgress(VendorGatePid).Money;
        Game.OnHandlePacket(_questSocketClient, listedVendorBuy);
        byte[][] listedBuyReplies = Enumerable.Range(0, 4)
            .Select(_ => ReadQuestSocketPacket()).ToArray();
        Check(listedBuyReplies[0][0] == 81 && listedBuyReplies[1][0] == 82
            && listedBuyReplies[2][0] == 16
            && listedBuyReplies[3].SequenceEqual(new BuyItem { Result = 0 }.Serialize())
            && _playerStore.GetProgress(VendorGatePid).Money < listedBuyMoney
            && _playerStore.GetInventory(VendorGatePid, () => []).Any(i => i.ItemId == 100101 && i.Count == 1),
            "actual Game buy packet purchases a cooked vendor item and publishes inventory and money");
        _questSocketClient.Player = _playerManager.Get(OfflineGuestPid);
        _questManager.SetPendingShare(OfflineGuestClientId, (OfflineHostPid, 820102));
        var shareDeny = new SquadNotify { Pid = OfflineGuestPid, Type = 0, Result = 2 }.Serialize();
        Game.OnHandlePacket(_questSocketClient, new byte[] { 87, 0xC8, 0x09, 0x0E, 0, 0, 2 });
        Check(!_questManager.HasPendingShare(OfflineGuestClientId)
            && ReadQuestSocketPacket().SequenceEqual(shareDeny)
            && ReadQuestSocketPacket().SequenceEqual(shareDeny),
            "active share-menu denial clears pending consent and broadcasts op97 to the squad");
        var shareAccept = new SquadNotify { Pid = OfflineGuestPid, Type = 0, Result = 1 }.Serialize();
        Game.OnHandlePacket(_questSocketClient, new byte[] { 87, 0xC8, 0x09, 0x0E, 0, 0, 1 });
        Check(ReadQuestSocketPacket().SequenceEqual(shareAccept)
            && ReadQuestSocketPacket().SequenceEqual(shareAccept),
            "share-menu acceptance broadcasts its post-acquire op97 status to the squad");
        // The guest's city TCP link is gone during mission travel, but its old client-ID quest
        // binding remains authoritative for listen-host reports about that participant.
        _detachedReportQuest = _questCatalog.All.First(x => x.QuestType == "QuestType_Free").EntryIndex;
        _detachedGuestClientId = OfflineGuestClientId + 100;
        _playerManager.SetOffline(OfflineGuestPid);
        _questSocketClient.Player = _playerManager.Get(OfflineHostPid);
        // The real mail button passes a local UI PlayerIndex where the native API names its first
        // integer PID. Exercise the production handler with that index colliding with a different
        // character PID; the typed recipient name must win.
        int beforeCorrectRecipientMail = _transmitStore.List(_ownedB.Pid).Count;
        int beforeWrongRecipientMail = _transmitStore.List(_ownedA.Pid).Count;
        _questSocketClient.Player = _playerManager.Get(_ownedA.Pid);
        using (var routedMail = new PacketWriter())
        {
            routedMail.Write((byte)180);
            routedMail.Write(_ownedA.Pid);
            routedMail.Write(_ownedB.Name);
            routedMail.Write("Routing regression");
            routedMail.Write("typed name wins");
            routedMail.Write(0);
            routedMail.Write(byte.MaxValue);
            routedMail.Write(byte.MaxValue);
            routedMail.Write(byte.MaxValue);
            Game.OnHandlePacket(_questSocketClient, routedMail.Buffer);
        }
        var routedMailReply = ReadQuestSocketPacket();
        var routedMoneyReply = ReadQuestSocketPacket();
        Check(routedMailReply.SequenceEqual(new TransmitSend { Answer = 0 }.Serialize())
            && routedMoneyReply[0] == 16
            && _transmitStore.List(_ownedB.Pid).Count == beforeCorrectRecipientMail + 1
            && _transmitStore.List(_ownedA.Pid).Count == beforeWrongRecipientMail,
            $"production mail-send packet reaches the named character despite a colliding UI index "
            + $"(mail={Convert.ToHexString(routedMailReply)}, money={Convert.ToHexString(routedMoneyReply)}, "
            + $"correct={_transmitStore.List(_ownedB.Pid).Count - beforeCorrectRecipientMail}, "
            + $"wrong={_transmitStore.List(_ownedA.Pid).Count - beforeWrongRecipientMail})");
        // Exercise the native op203/204 success and replay shapes through the encrypted Game socket,
        // not just the mailbox store. The client installs op203's 32-byte item itself.
        const int routedClaimPid = 920881;
        _questSocketClient.Player = _playerManager.Get(routedClaimPid);
        _playerStore.GetInventory(routedClaimPid, () => []);
        var routedClaimMail = _transmitStore.Add(routedClaimPid, 0, 3, "Quest NPC", "Reward", "Body", 321,
            attachments: [new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 604221, Count = 2 }]);
        int routedClaimItemBefore = _playerStore.GetInventory(routedClaimPid, () => [])
            .Where(item => item.ItemId == 604221).Sum(item => item.Count);
        using (var request = new PacketWriter())
        {
            request.Write((byte)183);
            request.Write(routedClaimMail.Sequence);
            request.Write(0);
            Game.OnHandlePacket(_questSocketClient, request.Buffer);
            byte[] claimedWire = ReadQuestSocketPacket();
            Check(claimedWire.Length == 42 && claimedWire[0] == 203 && claimedWire[1] == 0
                && BinaryPrimitives.ReadInt32LittleEndian(claimedWire.AsSpan(2)) == routedClaimMail.Sequence
                && BinaryPrimitives.ReadInt32LittleEndian(claimedWire.AsSpan(6)) == 0
                && BinaryPrimitives.ReadInt32LittleEndian(claimedWire.AsSpan(10)) == 604221
                && _playerStore.GetInventory(routedClaimPid, () => [])
                    .Where(item => item.ItemId == 604221).Sum(item => item.Count) == routedClaimItemBefore + 2,
                "mail item claim deposits once and sends the native full-item success callback");
            Game.OnHandlePacket(_questSocketClient, request.Buffer);
            Check(ReadQuestSocketPacket().SequenceEqual(new byte[] { 203, 8 })
                && _playerStore.GetInventory(routedClaimPid, () => [])
                    .Where(item => item.ItemId == 604221).Sum(item => item.Count) == routedClaimItemBefore + 2,
                "replayed mail item claim cannot mint another item");
        }
        long routedClaimMoneyBefore = _playerStore.GetProgress(routedClaimPid).Money;
        using (var request = new PacketWriter())
        {
            request.Write((byte)184);
            request.Write(routedClaimMail.Sequence);
            Game.OnHandlePacket(_questSocketClient, request.Buffer);
            byte[] crReply = ReadQuestSocketPacket();
            byte[] moneyRefresh = ReadQuestSocketPacket();
            Check(crReply.SequenceEqual(new TransmitGetCR { Answer = 0,
                    MessageSequence = routedClaimMail.Sequence }.Serialize())
                && moneyRefresh[0] == 16
                && _playerStore.GetProgress(routedClaimPid).Money == routedClaimMoneyBefore + 321,
                "mail CR claim credits once and refreshes the native money parameter");
            Game.OnHandlePacket(_questSocketClient, request.Buffer);
            Check(ReadQuestSocketPacket().SequenceEqual(new byte[] { 204, 8 })
                && _playerStore.GetProgress(routedClaimPid).Money == routedClaimMoneyBefore + 321,
                "replayed mail CR claim cannot credit money twice");
        }
        _questSocketClient.Player = _playerManager.Get(OfflineHostPid);
        _questRoomMembers.Detach(OfflineGuestPid, _detachedGuestClientId);
        _questRoomMembers.SetQuest(OfflineGuestClientId, _detachedReportQuest);
        _questRoomMembers.SetInstance(OfflineGuestClientId, OfflineHostPid);
        _questRoomMembers.SetQuest(_detachedGuestClientId, _detachedReportQuest);
        _questRoomMembers.SetInstance(_detachedGuestClientId, OfflineHostPid);
        _questInstanceManager.SetHost(OfflineHostPid, OfflineHostPid);
        ResetCharacter(OfflineHostPid);
        ResetCharacter(OfflineGuestPid);
        _playerStore.GetInventory(OfflineHostPid, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = 1, InventoryCell = 0, ItemId = 532201, Count = 2 });
        _playerStore.GetInventory(OfflineGuestPid, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = 1, InventoryCell = 0, ItemId = 532201, Count = 2 });
        SendListenBeltUse(_detachedReportQuest); // host PRI uses the room identity, not its character PID.
        Check(ReadQuestSocketPacket().SequenceEqual(new RemoveItem { InventoryIndex = 1, Cell = 0 }.Serialize())
            && ReadQuestSocketPacket().SequenceEqual(new InsertItem
                { InventoryIndex = 1, Cell = 0, ItemID = 532201, Count = 1, Durability = 100 }.Serialize())
            && _playerStore.GetInventory(OfflineHostPid, () => []).Single(i => i.InventoryIndex == 1).Count == 1,
            "encrypted listen-server op116 maps the host room PID to its real belt inventory");
        SendListenBeltUse(OfflineGuestPid);
        Check(_playerStore.GetInventory(OfflineGuestPid, () => []).Single(i => i.InventoryIndex == 1).Count == 1,
            "authenticated listen host reduces a detached same-instance guest's belt item");
        _questRoomMembers.SetInstance(_detachedGuestClientId, OfflineHostPid + 1);
        SendListenBeltUse(OfflineGuestPid);
        Check(_playerStore.GetInventory(OfflineGuestPid, () => []).Single(i => i.InventoryIndex == 1).Count == 1,
            "a squad member in another quest instance cannot consume the guest's belt item");
    }

    private void VendorExchangeAndQuestItems()
    {
        Check(!_playerStore.TryBuyItem(QtBuyerPid, _itemCatalog.Get(109201)!, 1, 1,
                out _, out long pcRoomMoneyAfter, out byte pcRoomFailure)
            && pcRoomFailure == 48 && pcRoomMoneyAfter == _pcRoomMoney
            && _playerStore.GetProgress(QtBuyerPid).Money == _pcRoomMoney
            && _playerStore.GetInventory(QtBuyerPid, () => []).All(i => i.ItemId != 109201),
            "vendor purchase returns native PC-room result without mutating money or inventory");
        Check(!_playerStore.TryBuyItem(QtBuyerPid, _itemCatalog.Get(120681)!, 1, 1,
                out _, out _, out byte expiredFailure) && expiredFailure == 7,
            "fixed-expiration items whose exported deadline elapsed cannot be purchased");
        const int medalBuyerPid = 920104;
        ResetCharacter(medalBuyerPid);
        _playerStore.GetInventory(medalBuyerPid, () => []);
        _playerStore.AddMoney(medalBuyerPid, 100_000);
        var medalWeapon = _itemCatalog.Get(130316)!;
        Check(medalWeapon.ExchangeItems.Count == 1
            && medalWeapon.ExchangeItems[0] == new ItemCatalog.ExchangeCost(609001, 14),
            "item catalog preserves authoritative ExchangeItems medal costs");
        Check(_playerStore.TryAddQuestItems(medalBuyerPid, [(609001, 13)], out _)
            && !_playerStore.TryBuyItem(medalBuyerPid, medalWeapon, 10, 1, out _, out _, out byte medalFailure)
            && medalFailure == 33
            && _playerStore.GetItems(medalBuyerPid).Single(x => x.item == 609001).num == 13,
            "vendor buy returns native insufficient-medal result without partial mutation");
        Check(_playerStore.TryAddQuestItems(medalBuyerPid, [(609001, 1)], out _)
            && _playerStore.TryBuyItem(medalBuyerPid, medalWeapon, 10, 1,
                out var medalPurchase, out long medalMoney, out byte medalSuccess)
            && medalSuccess == 0 && medalPurchase?.ItemId == 130316
            && medalMoney == 110_000 - medalWeapon.Price
            && !_playerStore.GetItems(medalBuyerPid).Any(x => x.item == 609001),
            "vendor buy atomically consumes CR and the exact exported medal exchange cost");
        Check(_playerStore.TrySellMainItem(TakeoffPid, 20, 1, _vendorData, out var afterSaleItem, out long afterSale)
            && afterSaleItem?.Count == 1 && afterSale == _afterBuy + 128,
            "vendor sell atomically applies retail sell price and decrements inventory");
        var zeroPriceData = _itemCatalog.Get(200399)!;
        _playerStore.GetInventory(TakeoffPid, () => []).Add(
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 21, ItemId = 200399, Count = 1, Durability = 42 });
        long beforeZeroPriceSale = _playerStore.GetProgress(TakeoffPid).Money;
        Check(zeroPriceData.SellPrice == 0
            && zeroPriceData.PermitSell
            && _playerStore.TrySellMainItem(TakeoffPid, 21, 1, zeroPriceData, out _, out long afterZeroPriceSale)
            && afterZeroPriceSale == beforeZeroPriceSale
            && !_playerStore.GetInventory(TakeoffPid, () => []).Any(i => i.InventoryIndex == 0 && i.InventoryCell == 21),
            "vendor sale eligibility follows retail bPermitSell even when proceeds are zero CR");
        var prohibitedSaleData = _itemCatalog.Get(512100)!;
        _playerStore.GetInventory(TakeoffPid, () => []).Add(
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 21, ItemId = 512100, Count = 1, Durability = 100 });
        Check(!prohibitedSaleData.PermitSell
            && !_playerStore.TrySellMainItem(TakeoffPid, 21, 1, prohibitedSaleData, out _, out long afterProhibitedSale,
                out _, out byte prohibitedSaleResult)
            && prohibitedSaleResult == PlayerStore.SellCannotSell
            && !_playerStore.TrySellMainItem(TakeoffPid, 21, 2, prohibitedSaleData, out _, out _, out _,
                out byte overCountResult) && overCountResult == PlayerStore.SellWrongParameter
            && !_playerStore.TrySellMainItem(TakeoffPid, 22, 1, prohibitedSaleData, out _, out _, out _,
                out byte emptyCellResult) && emptyCellResult == PlayerStore.SellNotExist
            && afterProhibitedSale == beforeZeroPriceSale
            && _playerStore.GetInventory(TakeoffPid, () => []).Any(i =>
                i.InventoryIndex == 0 && i.InventoryCell == 21 && i.ItemId == 512100 && i.Count == 1),
            "vendor rejects retail bPermitSell=false items without mutating inventory or money");
        int soldAggregate = _playerStore.GetItems(TakeoffPid).Single(x => x.item == 604221).num;
        Check(_playerStore.TryUndoSale(TakeoffPid,
                new PlayerStore.GenericItem { ItemId = 604221, Count = 1, Durability = 100 }, 128,
                out var undoDeposit, out long afterUndo)
            && undoDeposit.InventoryIndex == 0 && undoDeposit.Count == 1
            && afterUndo == _afterBuy
            && _playerStore.GetItems(TakeoffPid).Single(x => x.item == 604221).num == soldAggregate + 1,
            "vendor undo atomically charges exact proceeds and restores sold item state");
        ResetCharacter(_ownedA.Pid);
        _playerStore.GetProgress(_ownedA.Pid).Money = 1_000;
        Check(_economyRates.TrySetAuthoritative(_testRates)
            && _playerStore.TryBuyItem(_ownedA.Pid, _vendorData, 0, 2, out _, out long factionBuyMoney, out _)
            && factionBuyMoney == 680
            && _playerStore.TrySellMainItem(_ownedA.Pid, 0, 1, _vendorData, out _,
                out long factionSellMoney, out long factionSaleProceeds, out _)
            && factionSellMoney == 744 && factionSaleProceeds == 64
            && _playerStore.TryUndoSale(_ownedA.Pid,
                new PlayerStore.GenericItem { ItemId = _vendorData.EntryIndex, Count = 1 },
                factionSaleProceeds, out var factionUndoItem, out long factionUndoMoney)
            && factionUndoItem.ItemId == _vendorData.EntryIndex && factionUndoMoney == factionBuyMoney,
            "vendor sale and undo use the same faction-adjusted proceeds and retail truncation");
        ResetCharacter(_ownedA.Pid);
        _playerStore.GetInventory(_ownedA.Pid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100499, Durability = 90 }
        ]);
        _playerStore.GetProgress(_ownedA.Pid).QT = 1_000;
        Check(_playerStore.TryRepair(_ownedA.Pid, 3, 0, false, _licenseStore.RepairDiscountPercent(_ownedA.Pid), out _, out long factionRepairQt)
            && factionRepairQt == 967,
            "authoritative Sapiens repair rate multiplies the retail matrix before integer truncation");
        ResetCharacter(_ownedA.Pid);
        _playerStore.GetInventory(_ownedA.Pid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 128403, State = 1, TuneUpOptions = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 3, ItemId = 500001, Count = 100 }
        ]);
        _playerStore.AddItems(_ownedA.Pid, [(128403, 1), (500001, 100)]);
        Check(_playerStore.TryTuneUp(_ownedA.Pid, 1, _licenseStore.TuneupDiscountPercent(_ownedA.Pid), out _, out _, out _, optionOverride: 6) == 0
            // Single-precision 3000*1.3f is just below 3900 and the script's int assignment truncates.
            && _playerStore.GetProgress(_ownedA.Pid).Money == 6_101
            && _playerStore.GetInventory(_ownedA.Pid, () => []).Single(x => x.ItemId == 500001).Count == 64,
            "authoritative Sapiens tune-up rate multiplies both fee and lunarite count with retail truncation");
        _economyRates.ResetNeutral();
        Check(_playerStore.SwapSlots(TakeoffPid, 5, 3, 0, 20)
            && _playerStore.GetInventory(TakeoffPid, () => []).Any(i => i.InventoryIndex == 5 && i.InventoryCell == 3 && i.ItemId == 604221)
            && _playerStore.SwapSlots(TakeoffPid, 0, 21, 5, 3)
            && _playerStore.GetInventory(TakeoffPid, () => []).Any(i => i.InventoryIndex == 0 && i.InventoryCell == 21 && i.ItemId == 604221),
            "bank deposit and withdrawal persist MAIN/BANK slot moves");

        const int reducePid = 920003;
        ResetCharacter(reducePid);
        _playerStore.GetInventory(reducePid, () => []);
        Check(_playerStore.TryAddQuestItems(reducePid, [(604221, 5)], out _),
            "quest item insert succeeds with available MAIN capacity");
        Check(_playerStore.TryReduceItem(reducePid, 604221, 2)
            && _playerStore.GetItems(reducePid).Single(x => x.item == 604221).num == 3
            && _playerStore.GetInventory(reducePid, () => []).Single(x => x.ItemId == 604221).Count == 3,
            "quest item reduction updates aggregate and slotted persistence atomically");
        Check(_playerStore.TryReduceItem(reducePid, 604221, 0)
            && _playerStore.GetItems(reducePid).All(x => x.item != 604221)
            && _playerStore.GetInventory(reducePid, () => []).All(x => x.ItemId != 604221),
            "zero-quantity quest reduction removes every matching item");
        const int cityScriptInventoryPid = 920111;
        ResetCharacter(cityScriptInventoryPid);
        _playerStore.GetInventory(cityScriptInventoryPid, () => []);
        Check(_playerStore.TryAddQuestItems(cityScriptInventoryPid, [(613994, 2)], out var cityScriptCertificates)
            && cityScriptCertificates.Count == 2
            && _playerStore.SwapSlots(cityScriptInventoryPid, 5, 0, 0, cityScriptCertificates[0].InventoryCell)
            && _playerStore.TryReduceItem(cityScriptInventoryPid, 613994, 0, inventory: 0)
            && _playerStore.GetItems(cityScriptInventoryPid).Single(x => x.item == 613994).num == 1
            && _playerStore.GetInventory(cityScriptInventoryPid, () => [])
                .Single(x => x.ItemId == 613994) is { InventoryIndex: 5, InventoryCell: 0 },
            "city-script remove-all consumes only MAIN certificates and preserves the bank copy");
        Check(_playerStore.TryAddQuestItems(reducePid, [(820301, 1)], out var carriedQuestItem)
            && carriedQuestItem is [{ InventoryCell: 0, ItemId: 820301 }]
            && _playerStore.TryExchangeQuestItems(reducePid, [820301], [(604221, 1)],
                out var exchangedReward, out var exchangedCells)
            && exchangedCells.SequenceEqual(new byte[] { 0 })
            && exchangedReward is [{ InventoryCell: 0, ItemId: 604221 }]
            && _playerStore.GetItems(reducePid).All(x => x.item != 820301)
            && _playerStore.GetItems(reducePid).Single(x => x.item == 604221).num == 1,
            "quest completion atomically removes its carried item before placing the reward in the freed cell");
        Check(_playerStore.TryExchangeQuestItems(reducePid, [604221], [], out var noQuestRewards, out var retiredCells)
            && noQuestRewards.Count == 0 && retiredCells.SequenceEqual(new byte[] { 0 })
            && _playerStore.GetInventory(reducePid, () => []).All(x => x.ItemId != 604221),
            "quest retirement cleanup removes every configured temporary item without adding a reward");

        const int boundedInsertPid = 920009;
        ResetCharacter(boundedInsertPid);
        _playerStore.GetProgress(boundedInsertPid).InvenExpandStep = 2;
        _playerStore.GetInventory(boundedInsertPid, () => Enumerable.Range(0, 77)
            .Select(i => new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = (byte)i, ItemId = 700000 + i, Count = 1 })
            .ToList());
        Check(!_playerStore.TryAddQuestItems(boundedInsertPid, [(604221, 1), (609301, 1)], out var rejectedBatch)
            && rejectedBatch.Count == 0
            && _playerStore.GetItems(boundedInsertPid).Count == 0
            && _playerStore.GetInventory(boundedInsertPid, () => []).Count(i => i.InventoryIndex == 0) == 77,
            "quest item batch exceeding MAIN capacity rolls back aggregate and slotted state");
        Check(_playerStore.TryAddQuestItems(boundedInsertPid, [(604221, 1)], out var acceptedBatch)
            && acceptedBatch is [{ InventoryCell: 77, Durability: 0 }]
            && _playerStore.GetItems(boundedInsertPid).Single() == (604221, 1),
            "quest item batch fills the final retail MAIN cell and does not fabricate equipment durability");
        Check(!_playerStore.TryAddQuestItems(boundedInsertPid, [(999999, 1)], out var unknownBatch)
            && unknownBatch.Count == 0,
            "quest inserts reject item ids absent from the retail catalog");

        const int stackedInsertPid = 920008;
        ResetCharacter(stackedInsertPid);
        _playerStore.GetInventory(stackedInsertPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 604221, Count = 98 }
        ]);
        _playerStore.AddItems(stackedInsertPid, [(604221, 98)]);
        Check(_playerStore.TryAddQuestItems(stackedInsertPid, [(604221, 3)], out var stackedChanges)
            && stackedChanges.Count == 2
            && _playerStore.GetInventory(stackedInsertPid, () => []).Where(i => i.ItemId == 604221)
                .OrderBy(i => i.InventoryCell).Select(i => i.Count).SequenceEqual(new[] { 99, 2 })
            && _playerStore.GetItems(stackedInsertPid).Single() == (604221, 101),
            "quest insertion fills existing stacks and splits overflow at the exported retail stack limit");
        ResetCharacter(DurabilityPid);
        _playerStore.GetInventory(DurabilityPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100499, Durability = 100 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 4, ItemId = 200499, Durability = 100 }
        ]);
        Check(_playerStore.UpdateDurability(DurabilityPid, [(3, 0, 90), (4, 4, 76)]) == 2
            && _playerStore.GetInventory(DurabilityPid, () => []).Single(i => i.InventoryIndex == 3).Durability == 90
            && _playerStore.GetInventory(DurabilityPid, () => []).Single(i => i.InventoryIndex == 4).Durability == 76,
            "quest-result equipment durability persists by inventory and cell");
        Check(_playerStore.UpdateDurability(DurabilityPid, [(0, 0, 50), (3, 0, 101), (4, 4, 255)]) == 0
            && _playerStore.GetInventory(DurabilityPid, () => []).Single(i => i.InventoryIndex == 3).Durability == 90
            && _playerStore.GetInventory(DurabilityPid, () => []).Single(i => i.InventoryIndex == 4).Durability == 76,
            "quest-result durability rejects non-equipment inventories and values above retail 100");
        const int questWearPid = 920094;
        ResetCharacter(questWearPid);
        _playerStore.GetInventory(questWearPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100100, Durability = 80 },
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 100200, Durability = 5 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 4, ItemId = 200499, Durability = 100, Skill = 303400 }
        ]);
        Check(_playerStore.UpdateQuestDurabilityFromBoots(questWearPid, 91) == 3
            && _playerStore.GetInventory(questWearPid, () => []).Single(i => i.InventoryIndex == 3 && i.InventoryCell == 0).Durability == 71
            && _playerStore.GetInventory(questWearPid, () => []).Single(i => i.InventoryIndex == 3 && i.InventoryCell == 1).Durability == 0
            && _playerStore.GetInventory(questWearPid, () => []).Single(i => i.InventoryIndex == 4).Durability == 91,
            "type-4 quest settlement derives uniform equipped wear from trustworthy terminal boots durability");
        Check(_playerStore.UpdateQuestDurabilityFromBoots(questWearPid, 92) == 0,
            "type-4 quest settlement rejects a terminal durability increase");
        const int questWearUsedPid = 920096;
        ResetCharacter(questWearUsedPid);
        _playerStore.GetInventory(questWearUsedPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100100, Durability = 80 },
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 100200, Durability = 70 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 4, ItemId = 200499, Durability = 100, Skill = 303400 }
        ]);
        Check(_playerStore.UpdateQuestDurabilityFromBoots(questWearUsedPid, 95) == 3
            && _playerStore.GetInventory(questWearUsedPid, () => []).Single(i => i.InventoryIndex == 3 && i.InventoryCell == 0).Durability == 75
            && _playerStore.GetInventory(questWearUsedPid, () => []).Single(i => i.InventoryIndex == 3 && i.InventoryCell == 1).Durability == 65
            && _playerStore.GetInventory(questWearUsedPid, () => []).Single(i => i.InventoryIndex == 4).Durability == 95,
            "type-4 quest settlement wears every equipped weapon like retail, including ones without kills");
        const int nativeQuestWearPid = 920095;
        ResetCharacter(nativeQuestWearPid);
        _playerStore.GetInventory(nativeQuestWearPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100100, Durability = 80 },
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 100200, Durability = 70 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 200100, Durability = 90 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 4, ItemId = 200499, Durability = 100 }
        ]);
        Check(_playerStore.UpdateNativeQuestDurability(nativeQuestWearPid,
                [(3, 0, 76), (3, 1, 0), (4, 0, 87), (4, 4, 96)]) == 3
            && _playerStore.GetInventory(nativeQuestWearPid, () => [])
                .Single(i => i.InventoryIndex == 3 && i.InventoryCell == 0).Durability == 76
            && _playerStore.GetInventory(nativeQuestWearPid, () => [])
                .Single(i => i.InventoryIndex == 3 && i.InventoryCell == 1).Durability == 70
            && _playerStore.GetInventory(nativeQuestWearPid, () => [])
                .Single(i => i.InventoryIndex == 4 && i.InventoryCell == 0).Durability == 87
            && _playerStore.GetInventory(nativeQuestWearPid, () => [])
                .Single(i => i.InventoryIndex == 4 && i.InventoryCell == 4).Durability == 96,
            "native-backup quest result applies slot-specific armor and used weapon wear without breaking an unused weapon");
        Check(_playerStore.UpdateNativeQuestDurability(nativeQuestWearPid,
                [(3, 0, 80), (4, 4, 100), (3, 4, 1), (4, 5, 1), (4, 0, 101)]) == 0
            && _playerStore.UpdateNativeQuestDurability(nativeQuestWearPid,
                [(4, 0, 87), (4, 4, 96)]) == 0,
            "native-backup quest result rejects stale increases, invalid slots, and replayed wear");
        _corruptDurabilityPid = 920093;
        ResetCharacter(_corruptDurabilityPid);
        _playerStore.GetInventory(_corruptDurabilityPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100100, Durability = 140 },
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 100200, Durability = 0 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 4, ItemId = 200499, Durability = 0, Skill = 303400 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 604001, Durability = 0 }
        ]);
        Check(_playerStore.MigrateCorruptMissionDurability() >= 3
            && _playerStore.GetInventory(_corruptDurabilityPid, () => []).Where(i => i.InventoryIndex is 3 or 4)
                .All(i => i.Durability == 100)
            && _playerStore.GetInventory(_corruptDurabilityPid, () => []).Single(i => i.InventoryIndex == 0).Durability == 0,
            "legacy mission-durability migration repairs contaminated equipped gear without reviving ordinary items");
    }
}
