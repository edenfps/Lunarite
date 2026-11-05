using Lunarite.Packets.Common;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void TuneUpCostDefaults()
    {
        // Default__HuxleyWeaponData / Default__HuxleyArmorData: TuneupLunaritesA=3, TuneupFee=50,
        // each inherited independently when the record has no line of its own.
        Check(_itemCatalog.Get(100101) is { TuneupFee: 225, TuneupLunaritesA: 3 }
            && _itemCatalog.Get(140607) is { TuneupFee: 2500, TuneupLunaritesA: 3, TuneupLunaritesB: 3 }
            && _itemCatalog.Get(100299) is { TuneupFee: 50, TuneupLunaritesA: 3 }
            && _itemCatalog.Get(230097) is { TuneupFee: 2500, TuneupLunaritesA: 3 },
            "weapon and armor tune-up fee and A-lunarites inherit the class defaults per property");

        const int tunePid = 925301;
        ResetCharacter(tunePid);
        _playerStore.GetInventory(tunePid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 100101 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 2, ItemId = 140607 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 3, ItemId = 230097 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 100299 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 5, ItemId = 500001, Count = 99 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 6, ItemId = 500002, Count = 99 }
        ]);
        _playerStore.AddItems(tunePid, [(100101, 1), (140607, 1), (230097, 1), (100299, 1), (500001, 99), (500002, 99)]);
        _playerStore.GetProgress(tunePid).Money = 100_000;
        int LunaritesOf(int id) => _playerStore.GetInventory(tunePid, () => []).Where(x => x.ItemId == id).Sum(x => x.Count);
        bool Tuned(byte cell, long fee, int lunarA)
        {
            long money = _playerStore.GetProgress(tunePid).Money;
            int a = LunaritesOf(500001), b = LunaritesOf(500002);
            return _playerStore.TryTuneUp(tunePid, cell, 0, out _, out _, out _, optionOverride: 0) == 0
                && _playerStore.GetProgress(tunePid).Money == money - fee
                && LunaritesOf(500001) == a - lunarA && LunaritesOf(500002) == b;
        }
        Check(Tuned(1, 225, 3) && Tuned(2, 2500, 3) && Tuned(3, 2500, 3) && Tuned(4, 50, 3),
            "tune-up charges the inherited A-lunarites the client lists, never B, and accepts default-fee weapons");
    }

    private void SameInventoryMergeRules()
    {
        const int mergePid = 925302;
        ResetCharacter(mergePid);
        _playerStore.GetInventory(mergePid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 100100, TuneUpOptions = 3 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 100100, Durability = 40 },
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100100, TuneUpOptions = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 1, ItemId = 100100 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 604221, Count = 60 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 5, ItemId = 604221, Count = 70 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 6, ItemId = 604221, Count = 99 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 7, ItemId = 604221, Count = 5, Durability = 0 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 8, ItemId = 604221, Count = 5 }
        ]);
        PlayerStore.GenericItem? At(byte inven, byte cell) =>
            _playerStore.GetInventory(mergePid, () => []).SingleOrDefault(x => x.InventoryIndex == inven && x.InventoryCell == cell);

        Check(_playerStore.TryMoveWithinInventory(mergePid, InventoryType.Main, 0, 1, 0, out bool mainRefresh, out _, out _)
            && !mainRefresh
            && At(0, 0) is { ItemId: 100100, Count: 1, Durability: 40, TuneUpOptions: 0 }
            && At(0, 1) is { ItemId: 100100, Count: 1, TuneUpOptions: 3 },
            "identical non-stackable weapons in MAIN swap instead of fusing into a count-2 item");
        Check(_playerStore.TryMoveWithinInventory(mergePid, InventoryType.Weapon, 0, 1, 0, out bool weaponRefresh, out _, out _)
            && !weaponRefresh
            && At(3, 0) is { ItemId: 100100, Count: 1, TuneUpOptions: 0 }
            && At(3, 1) is { ItemId: 100100, Count: 1, TuneUpOptions: 1 },
            "swapping two weapon slots that hold the same weapon keeps both weapons");
        Check(_playerStore.TryMoveWithinInventory(mergePid, InventoryType.Main, 5, 4, 70, out bool capRefresh,
                out var capSource, out var capTarget)
            && capRefresh && capSource?.Count == 31 && capTarget?.Count == 99,
            "stack merge stops at MaxCollapseCount and leaves the rest in the source cell");
        Check(_playerStore.TryMoveWithinInventory(mergePid, InventoryType.Main, 6, 4, 99, out bool fullRefresh, out _, out _)
            && !fullRefresh && At(0, 4)?.Count == 99 && At(0, 6)?.Count == 99,
            "a full stack dropped on a full stack swaps without exceeding MaxCollapseCount");
        Check(_playerStore.TryMoveWithinInventory(mergePid, InventoryType.Main, 7, 8, 5, out bool consumableRefresh, out _, out _)
            && consumableRefresh && At(0, 7) == null && At(0, 8)?.Count == 10,
            "consumable stacks merge even when one carries durability 0 and the other the default 100");
    }
}
