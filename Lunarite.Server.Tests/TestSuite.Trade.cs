using System.Buffers.Binary;

using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void PlayerTrade()
    {
        Check(new TradeFail().Serialize().SequenceEqual(new byte[] { 85, 3 }),
            "server op85 trade-failure branch is exact");
        Check(TradeManager.IsPlayerTradePresenceValid(true, true, 0, 0, 2, 2)
            && !TradeManager.IsPlayerTradePresenceValid(false, true, 0, 0, 2, 2)
            && !TradeManager.IsPlayerTradePresenceValid(true, false, 0, 0, 2, 2)
            && !TradeManager.IsPlayerTradePresenceValid(true, true, 0, 0, 2, 3)
            && !TradeManager.IsPlayerTradePresenceValid(true, true, 9, 9, 2, 2),
            "direct trade requires two spawned players in the same city, not a quest or loading world");
        var pendingTrades = new Dictionary<int, int> { [22] = 11 };
        Check(TradeManager.IsTradeRequestAvailable(33, 44, pendingTrades, Array.Empty<int>())
            && !TradeManager.IsTradeRequestAvailable(11, 44, pendingTrades, Array.Empty<int>())
            && !TradeManager.IsTradeRequestAvailable(33, 22, pendingTrades, Array.Empty<int>())
            && !TradeManager.IsTradeRequestAvailable(22, 44, pendingTrades, Array.Empty<int>())
            && !TradeManager.IsTradeRequestAvailable(33, 11, pendingTrades, Array.Empty<int>())
            && !TradeManager.IsTradeRequestAvailable(33, 44, pendingTrades, new[] { 44 })
            && TradeManager.IsTradeAcceptanceAvailable(22, 11, pendingTrades, Array.Empty<int>())
            && !TradeManager.IsTradeAcceptanceAvailable(22, 11, pendingTrades, new[] { 11 })
            && !TradeManager.IsTradeAcceptanceAvailable(22, 33, pendingTrades, Array.Empty<int>()),
            "trade request and acceptance reject parallel pending or active sessions for either participant");
        Check(TradeManager.TradeCancellationRecipients(11, pendingTrades, 0).SequenceEqual(new[] { 22 })
            && TradeManager.TradeCancellationRecipients(22, pendingTrades, 0).SequenceEqual(new[] { 11 })
            && TradeManager.TradeCancellationRecipients(33, pendingTrades, 0).Count == 0
            && TradeManager.TradeCancellationRecipients(11, pendingTrades, 44).SequenceEqual(new[] { 22, 44 })
            && TradeManager.TradeCancellationRecipients(11, new Dictionary<int, int> { [22] = 11, [44] = 11 }, 22)
                .SequenceEqual(new[] { 22, 44 }),
            "trade cancellation notifies only recorded counterparts without duplicate recipients");
        var tradeItemWire = new Trade
        {
            Action = 5, Result = 1, Slot = 2,
            Item = new byte[32]
        }.Serialize();
        Check(tradeItemWire.Length == 36 && tradeItemWire[0] == 85 && tradeItemWire[1] == 5
            && tradeItemWire[2] == 1 && tradeItemWire[3] == 2,
            "server op85 trade-add emits native result/slot/32-byte GenericItem layout");
        Check(new Trade { Action = 0, Pid = 77, Name = "Trader" }.Serialize()
                .SequenceEqual(new byte[] { 85, 0, 77, 0, 0, 0, 84, 114, 97, 100, 101, 114, 0 }),
            "server op85 trade request emits native PID/string body");
        const int tradeA = 920050, tradeB = 920051;
        ResetCharacter(tradeA); ResetCharacter(tradeB);
        _playerStore.GetInventory(tradeA, () => [new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 604221, Count = 3 }]);
        _playerStore.GetInventory(tradeB, () => [new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 8, ItemId = 604222, Count = 2 }]);
        Check(PlayerStore.TradeSlotCapacity == 7
            && _playerStore.CompleteTrade(tradeA, new Dictionary<byte, byte> { [7] = 4 }, 0,
                tradeB, new Dictionary<byte, byte>(), 0) == PlayerStore.TradeCommitResult.Invalid
            && _playerStore.GetInventory(tradeA, () => []).Single().ItemId == 604221,
            "direct trade rejects offer slot 7 outside the exported seven-slot menu");
        var tradeABefore = _playerStore.GetInventory(tradeA, () => []).Select(item => new PlayerStore.GenericItem
            { InventoryIndex = item.InventoryIndex, InventoryCell = item.InventoryCell, ItemId = item.ItemId, Count = item.Count }).ToArray();
        Check(_playerStore.TryCompleteTrade(tradeA, new Dictionary<byte, byte> { [0] = 4 }, 1200,
                tradeB, new Dictionary<byte, byte> { [1] = 8 }, 300),
            "two-player trade commits both offers atomically");
        Check(_playerStore.GetInventory(tradeA, () => []).Single().ItemId == 604222
            && _playerStore.GetInventory(tradeB, () => []).Single().ItemId == 604221
            && _playerStore.GetProgress(tradeA).Money == 9100
            && _playerStore.GetProgress(tradeB).Money == 10900,
            "trade swaps full item state and nets both money offers");
        var tradeASettlement = TradeManager.BuildTradeSettlementPackets(tradeABefore,
            _playerStore.GetInventory(tradeA, () => []), _playerStore.GetProgress(tradeA).Money);
        Check(tradeASettlement.Count == 4
            && tradeASettlement[0].Serialize().SequenceEqual(new byte[] { 85, 9 })
            && tradeASettlement[1] is InsertItem { InventoryIndex: 0, Cell: 0, ItemID: 604222, Count: 2 }
            && tradeASettlement[2] is RemoveItem { InventoryIndex: 0, Cell: 4 }
            && tradeASettlement[3] is Parameter { Param: Parameter.PARAMETER_MONEY, Value: 9100 },
            "completed player trade closes the UI then publishes exact item-cell and money changes");
        var occupiedTradeCell = TradeManager.BuildTradeSettlementPackets(
            [new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 2, ItemId = 604221, Count = 1 }],
            [new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 2, ItemId = 604222, Count = 2,
                State = 2, LimitTime = 12345, TuneUpOptions = 6, Durability = 77 }], 42);
        Check(occupiedTradeCell is [Trade { Action: 9 }, RemoveItem { InventoryIndex: 0, Cell: 2 },
                InsertItem { InventoryIndex: 0, Cell: 2, ItemID: 604222, Count: 2, State: 2,
                    LimitTime: 12345, TuneUpOptions: 6, Durability: 77 },
                Parameter { Param: Parameter.PARAMETER_MONEY, Value: 42 }],
            "trade settlement clears an occupied MAIN cell, then inserts the full acquired item record");
        Check(!_playerStore.TryCompleteTrade(tradeA, new Dictionary<byte, byte> { [0] = 99 }, 0,
                tradeB, new Dictionary<byte, byte>(), 0)
            && _playerStore.GetInventory(tradeA, () => []).Single().ItemId == 604222,
            "stale trade offer fails without partially changing either inventory");
        var tradeBound = _playerStore.GetInventory(tradeA, () => []).Single();
        tradeBound.State = 0x01;
        Check(!_playerStore.TryCompleteTrade(tradeA, new Dictionary<byte, byte> { [0] = tradeBound.InventoryCell }, 0,
                tradeB, new Dictionary<byte, byte>(), 0)
            && _playerStore.GetInventory(tradeA, () => []).Single().ItemId == 604222,
            "player trade rejects bound items without partially changing either inventory");
        tradeBound.State = 0;
        tradeBound.ItemId = 100499;
        tradeBound.Durability = 77;
        Check(!_playerStore.CanPlayerTradeItem(tradeBound)
            && !_playerStore.TryCompleteTrade(tradeA, new Dictionary<byte, byte> { [0] = tradeBound.InventoryCell }, 0,
                tradeB, new Dictionary<byte, byte>(), 0),
            "player trade rejects damaged repairable equipment at offer and atomic commit");
        tradeBound.Durability = 100;
        Check(_playerStore.CanPlayerTradeItem(tradeBound),
            "player trade accepts fully repaired equipment as the exported menu does");
        const int repairedGearSeller = 920054, repairedGearBuyer = 920055;
        ResetCharacter(repairedGearSeller);
        ResetCharacter(repairedGearBuyer);
        _playerStore.GetInventory(repairedGearBuyer, () => []);
        _playerStore.GetInventory(repairedGearSeller, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = 0, InventoryCell = 2, ItemId = 100499, Count = 1, Durability = 100, TuneUpOptions = 7 });
        Check(_playerStore.TryCompleteTrade(repairedGearSeller, new Dictionary<byte, byte> { [0] = 2 }, 0,
                repairedGearBuyer, new Dictionary<byte, byte>(), 0)
            && _playerStore.GetInventory(repairedGearSeller, () => []).All(item => item.ItemId != 100499)
            && _playerStore.GetInventory(repairedGearBuyer, () => []).Any(item => item.ItemId == 100499
                && item.Durability == 100 && item.TuneUpOptions == 7),
            "player trade atomically transfers fully repaired gear with its item state intact");
        tradeBound.ItemId = 109201;
        Check(!_playerStore.CanPlayerTradeItem(tradeBound)
            && !_playerStore.TryCompleteTrade(tradeA, new Dictionary<byte, byte> { [0] = tradeBound.InventoryCell }, 0,
                tradeB, new Dictionary<byte, byte>(), 0),
            "player trade rejects exported EISC_PCRoom items at offer and atomic commit");
        const int tradeFullA = 920052, tradeSourceB = 920053;
        ResetCharacter(tradeFullA); ResetCharacter(tradeSourceB);
        _playerStore.GetInventory(tradeFullA, () => Enumerable.Range(0, _playerStore.MainCapacity(tradeFullA))
            .Select(cell => new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = (byte)cell, ItemId = 604221, Count = 1 }).ToList());
        _playerStore.GetInventory(tradeSourceB, () =>
            [new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 604222, Count = 1 }]);
        Check(_playerStore.CompleteTrade(tradeFullA, new Dictionary<byte, byte>(), 0,
                tradeSourceB, new Dictionary<byte, byte> { [0] = 0 }, 0)
                == PlayerStore.TradeCommitResult.AInventoryFull
            && _playerStore.GetInventory(tradeSourceB, () => []).Single().ItemId == 604222,
            "trade commit distinguishes A-side inventory full without mutating either player");
        Check(new NewMember { Answer = 0 }.Serialize().SequenceEqual(new byte[] { 6, 0 }),
            "account creation callback serializes native op6 success answer");
        Check(new TuneUp { Result = 0, Option = 3 }.Serialize().SequenceEqual(new byte[] { 212, 0, 3 })
            && new UpdateItemTuneUp { Inventory = 0, Cell = 7, TuneUpOptions = 0x1000 }.Serialize()
                .SequenceEqual(new byte[] { 213, 0, 7, 0, 0x10, 0, 0 })
            && new ResetTuneUp { Result = 0, ResetType = 1, Option = 0 }.Serialize()
                .SequenceEqual(new byte[] { 228, 0, 1, 0 })
            && new RemoveItemBind { Result = 1 }.Serialize().SequenceEqual(new byte[] { 229, 1 }),
            "tune-up/reset/bind responses match native conditional layouts");
        Check(new ItemLimitTimeUpdate { Inventory = 1, Cell = 2, LimitTime = 0x12345678 }.Serialize()
                .SequenceEqual(new byte[] { 218, 1, 2, 0x78, 0x56, 0x34, 0x12 }),
            "item limit-time update matches native op218 bb+d layout");
        var itemStateBatch = new ItemStateBatchUpdate();
        itemStateBatch.Entries.Add(new ItemStateEntry(0, 4, 0x20));
        itemStateBatch.Entries.Add(new ItemStateEntry(3, 1, 0x02));
        Check(itemStateBatch.Serialize().SequenceEqual(new byte[] { 78, 2, 0, 4, 0x20, 3, 1, 0x02 }),
            "item-state batch update matches native op78 count plus three-byte records");
        var replacementWire = new ItemReplacement
        {
            RemovedInventory = 0, RemovedCell = 4,
            Item = new InsertItem { ItemID = 120689, Count = 2, InventoryIndex = 0, Cell = 4,
                State = 1, LimitTime = 0x12345678, TuneUpOptions = 3, Skill0 = 4, Skill1 = 5, Durability = 90 }
        }.Serialize();
        Check(replacementWire.Length == 35 && replacementWire[0] == 217
            && replacementWire[1] == 0 && replacementWire[2] == 4
            && BinaryPrimitives.ReadInt32LittleEndian(replacementWire.AsSpan(3)) == 120689
            && BinaryPrimitives.ReadInt32LittleEndian(replacementWire.AsSpan(7)) == 2
            && replacementWire.AsSpan(3).SequenceEqual(new InsertItem { ItemID = 120689, Count = 2,
                InventoryIndex = 0, Cell = 4, State = 1, LimitTime = 0x12345678,
                TuneUpOptions = 3, Skill0 = 4, Skill1 = 5, Durability = 90 }.Serialize().AsSpan(1)),
            "item replacement matches native op217 bb plus exact 32-byte GenericItem layout");
        var authoritativeWeaponBefore = new PlayerStore.GenericItem
            { InventoryIndex = 3, InventoryCell = 0, ItemId = 100499, Count = 1, Durability = 17 };
        var authoritativeWeaponAfter = new PlayerStore.GenericItem
        {
            InventoryIndex = 3, InventoryCell = 0, ItemId = 200499, Count = 1, State = 2,
            LimitTime = 1234, TuneUpOptions = 5, Skill = 6, Skill1 = 7, Durability = 83
        };
        var authoritativeWeaponChanges = ItemManager.BuildInventorySlotChanges(
            [authoritativeWeaponBefore], [authoritativeWeaponAfter], 3);
        Check(authoritativeWeaponChanges is [ItemReplacement authoritativeReplacement]
            && authoritativeReplacement.RemovedInventory == 3 && authoritativeReplacement.RemovedCell == 0
            && authoritativeReplacement.Item.ItemID == 200499
            && authoritativeReplacement.Item.State == 2
            && authoritativeReplacement.Item.LimitTime == 1234
            && authoritativeReplacement.Item.TuneUpOptions == 5
            && authoritativeReplacement.Item.Skill0 == 6
            && authoritativeReplacement.Item.Skill1 == 7
            && authoritativeReplacement.Item.Durability == 83,
            "combat-style slot replacement publishes one atomic op217 with authoritative full item state");
        var mixedSlotChanges = ItemManager.BuildInventorySlotChanges(
            [authoritativeWeaponBefore, new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 2, ItemId = 100401 }],
            [authoritativeWeaponBefore, new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 200401 }], 3);
        Check(mixedSlotChanges.Count == 2 && mixedSlotChanges[0] is InsertItem { Cell: 1, ItemID: 200401 }
            && mixedSlotChanges[1] is RemoveItem { Cell: 2, InventoryIndex: 3 },
            "slot-diff builder preserves unchanged weapons and emits exact insert/remove callbacks");
        const int starterReplacementPid = 930217;
        _playerStore.GetInventory(starterReplacementPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100100, Count = 1,
                State = 2, LimitTime = 1234, TuneUpOptions = 5, Skill = 6, Skill1 = 7, Durability = 83 },
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 100200, Count = 1, Durability = 44 }
        ]);
        _playerStore.ReplaceInventorySlots(starterReplacementPid, 3, [100100, 100501]);
        var replacedStarterWeapons = _playerStore.GetInventory(starterReplacementPid, () => []);
        var retainedStarterWeapon = replacedStarterWeapons.Single(item => item.InventoryIndex == 3 && item.InventoryCell == 0);
        var grantedStarterWeapon = replacedStarterWeapons.Single(item => item.InventoryIndex == 3 && item.InventoryCell == 1);
        Check(retainedStarterWeapon.ItemId == 100100 && retainedStarterWeapon.State == 2
            && retainedStarterWeapon.LimitTime == 1234 && retainedStarterWeapon.TuneUpOptions == 5
            && retainedStarterWeapon.Skill == 6 && retainedStarterWeapon.Skill1 == 7
            && retainedStarterWeapon.Durability == 83 && grantedStarterWeapon.ItemId == 100501
            && grantedStarterWeapon.Count == 1 && grantedStarterWeapon.Durability == 100,
            "style loadout retains unchanged ownership records and grants replacements through item rules");
        Check(new NotifyExpirationItem { ItemId = 120689, TuneUpOptions = 3, Type = 0, AfterSelectPlayer = true }
                .Serialize().SequenceEqual(new byte[] { 219, 0x71, 0xD7, 1, 0, 3, 0, 0, 0, 0, 1 }),
            "item expiration notice matches native op219 ddbb layout");
    }
}
