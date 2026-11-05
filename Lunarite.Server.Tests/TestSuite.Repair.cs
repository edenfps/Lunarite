using Lunarite.Packets.Outgoing;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void RepairAndSquadDirectory()
    {
        _playerStore.DeleteCharacter(_corruptDurabilityPid);
        _playerStore.GetProgress(DurabilityPid).QT = 10_000;
        Check(_playerStore.TryRepair(DurabilityPid, 3, 0, false, _licenseStore.RepairDiscountPercent(DurabilityPid), out var repaired, out long repairQt)
            && repaired is [{ InventoryIndex: 3, InventoryCell: 0, Durability: 100 }] && repairQt == 9_970
            && _playerStore.GetInventory(DurabilityPid, () => []).Single(i => i.InventoryIndex == 3).Durability == 100,
            "single-item repair atomically applies the retail QT matrix and restores durability");
        _licenseStore.Reset(DurabilityPid);
        Check(_licenseStore.Grant(DurabilityPid, 723723)
            && _playerStore.TryRepair(DurabilityPid, 4, 4, false, _licenseStore.RepairDiscountPercent(DurabilityPid), out var discountedRepair, out long discountedRepairQt)
            && discountedRepair is [{ InventoryIndex: 4, InventoryCell: 4, Durability: 100 }]
            && discountedRepairQt == 9_913,
            "repair applies the exported 20-percent RepairTalent multiplier before QT deduction");
        Check(new UpdateRepair { Inventory = 3, Cell = 0 }.Serialize().SequenceEqual(new byte[] { 88, 3, 0 })
            && new UpdateRepair { Inventory = 7 }.Serialize().SequenceEqual(new byte[] { 88, 7 })
            && new UpdateRepair { Inventory = byte.MaxValue }.Serialize().SequenceEqual(new byte[] { 88, 255 })
            && new UpdateRepair { Inventory = 8 }.Serialize().SequenceEqual(new byte[] { 88, 8 }),
            "server op88 serializes exact single/all/equipped/failure discriminators");
        Check(new SquadState { State = 0 }.Serialize().SequenceEqual(new byte[] { 98, 0 })
            && new EmptySquadMemberList().Serialize().SequenceEqual(new byte[] { 100, 0 }),
            "no-squad refresh emits exact state and zero-member packets");
        Check(new SquadList { PageDirection = 0 }.Serialize().SequenceEqual(new byte[] { 99, 0, 0, 0 })
            && new SquadList { PageDirection = 1 }.Serialize().SequenceEqual(new byte[] { 99, 1, 0, 0, 0 }),
            "empty squad directory follows native PAGE_BEGIN/PAGE_CUR packet branches");
        var populatedSquads = new SquadList { PageDirection = 1, CurrentPage = 1, TotalPages = 1 };
        populatedSquads.Entries.Add(new SquadList.Entry(7, "Alpha", 1));
        Check(populatedSquads.Serialize().SequenceEqual(new byte[] { 99, 1, 1, 1, 1, 7, 0, 0, 0, 65, 108, 112, 104, 97, 0, 1 }),
            "squad directory emits native dsb entries");
    }
}
