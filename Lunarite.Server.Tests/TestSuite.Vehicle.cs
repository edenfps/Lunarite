using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void VehiclesAndRecipes()
    {
        var pingGrades = new UpdatePingTime();
        pingGrades.Entries.Add((0x1234, 2)); pingGrades.Entries.Add((0xABCD, 4));
        Check(pingGrades.Serialize().SequenceEqual(new byte[] { 236, 2, 0, 0x34, 0x12, 2, 0xCD, 0xAB, 4 }),
            "server op236 serializes native word-count plus packed relay-key/ping-grade entries");
        Check(new AddVehicle { MainCell = 7 }.Serialize().SequenceEqual(new byte[] { 172, 0, 7 })
            && new RemoveVehicle { MainCell = 7 }.Serialize().SequenceEqual(new byte[] { 173, 0, 7, 0xFF })
            && new SummonVehicle { Result = 6 }.Serialize().SequenceEqual(new byte[] { 174, 6 })
            && new UpdateVehicle { Pid = 7, VehicleItemId = 608001 }.Serialize()
                .SequenceEqual(new byte[] { 175, 7, 0, 0, 0, 1, 71, 9, 0 })
            && new UpdateVehicle { Pid = 7, VehicleItemId = 0 }.Serialize()
                .SequenceEqual(new byte[] { 175, 7, 0, 0, 0, 0, 0, 0, 0 }),
            "vehicle add/remove/summon responses and observer update match native packet layouts");
        const int vehiclePid = 920012;
        ResetCharacter(vehiclePid);
        var starterVehicle = _itemCatalog.Get(608001)!;
        var level20Vehicle = _itemCatalog.Get(628001)!;
        var starterVehicleItem = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 7, ItemId = 608001, Count = 1 };
        Check(_equipmentRules.ValidateVehicle(vehiclePid, 7, starterVehicleItem, starterVehicle) == 0
            && _equipmentRules.ValidateVehicle(vehiclePid, 7, null, null) == 6
            && _equipmentRules.ValidateVehicle(vehiclePid, 7, starterVehicleItem, _itemCatalog.Get(604221)) == 5,
            "vehicle equip distinguishes valid, missing, and non-vehicle items with native city-vehicle results");
        Check(level20Vehicle.EquipLevel == 20
            && _equipmentRules.ValidateVehicle(vehiclePid, 7,
                new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 7, ItemId = 628001, Count = 1 }, level20Vehicle) == 1,
            "vehicle equip rejects a forged level-20 vehicle request at level 1 with native mismatch result");
        _playerStore.GetInventory(vehiclePid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 7, ItemId = 608001, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 8, ItemId = 604221, Count = 1 }
        ]);
        Check(_playerStore.SwapSlots(vehiclePid, 2, 0, 0, 7)
            && _playerStore.GetInventory(vehiclePid, () => []).Any(x => x.InventoryIndex == 2 && x.InventoryCell == 0 && x.ItemId == 608001),
            "vehicle equip persists MAIN-to-vehicle inventory movement");
        Check(_playerStore.TryTakeOff(vehiclePid, 2, 0, 8, out byte vehicleMainCell, out byte vehicleRelocatedCell)
            && vehicleMainCell == 8 && vehicleRelocatedCell != 0xFF
            && _playerStore.GetInventory(vehiclePid, () => []).Any(x => x.InventoryIndex == 0 && x.InventoryCell == 8 && x.ItemId == 608001),
            "vehicle removal atomically relocates an occupied requested MAIN cell");
        Check(_craftCatalog.Get(616400) is { CraftCost: 1200, MakeItemIndex: 110408 } craft616400
            && craft616400.Materials.Select(x => (x.ItemId, x.Count)).SequenceEqual(
                new[] { (613005, 4), (613007, 2), (613041, 1), (613004, 1) }),
            "regenerated UE3 item dump preserves exact craft materials and output");
        Check(_craftStore.Learn(920005, 616400) || _craftStore.Knows(920005, 616400),
            "learned craft recipe persists by character");
        _craftListWire = new RequestCraftRecipe { Recipes = [611108, 616400] }.Serialize();
    }
}
