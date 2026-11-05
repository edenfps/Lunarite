using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void InventoryCellBoundsAndArrange()
    {
        const int boundsPid = 920600;
        ResetCharacter(boundsPid);
        _playerStore.GetInventory(boundsPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 500001, Count = 10 },
            new PlayerStore.GenericItem { InventoryIndex = 5, InventoryCell = 0, ItemId = 604221, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 200199, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 6, InventoryCell = 0, ItemId = 303400, Count = 1 }
        ]);
        Check(!_playerStore.TryMoveWithinInventory(boundsPid, InventoryType.Main, 0, 200, 1, out _, out _, out _)
            && !_playerStore.TryMoveWithinInventory(boundsPid, InventoryType.Main, 0, 48, 0, out _, out _, out _)
            && !_playerStore.TryMoveWithinInventory(boundsPid, InventoryType.Bank, 0, 10, 0, out _, out _, out _)
            && _playerStore.GetInventory(boundsPid, () => [])
                .Count(i => i.InventoryIndex is 0 or 5 && i.InventoryCell == 0) == 2,
            "op76 refuses MAIN and BANK cells past the inventory's capacity");
        Check(!_playerStore.TryMoveWithinInventory(boundsPid, InventoryType.Armor, 0, 1, 0, out _, out _, out _)
            && !_playerStore.TryMoveWithinInventory(boundsPid, InventoryType.Skill, 0, 1, 0, out _, out _, out _)
            && _playerStore.TryMoveWithinInventory(boundsPid, InventoryType.Main, 0, 47, 4, out _, out _, out _),
            "op76 only moves within MAIN, BANK and weapon slots, never armor or socketed skill cells");
        const int dropPid = 939870010;
        _characterStore.Create(dropPid, "SkillDropper", 1, new Custom());
        ResetCharacter(dropPid);
        _playerStore.GetInventory(dropPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 6, InventoryCell = 0, ItemId = 303400, Count = 1 }
        ]);
        using (var dropLink = OpenSocialGameSocket(dropPid))
        {
            _playerManager.SetOnline(dropPid, dropLink.Client);
            dropLink.Client.Player = _playerManager.Get(dropPid);
            using var drop = new PacketWriter();
            drop.Write((byte)66); drop.Write(InventoryType.Skill); drop.Write((byte)0); drop.Write(1);
            Game.OnHandlePacket(dropLink.Client, drop.Buffer);
            Check(ReceivesSocial(dropLink, p => p.SequenceEqual(new DropItem { Result = 1 }.Serialize()))
                && _playerStore.GetInventory(dropPid, () => []).Any(i => i.InventoryIndex == 6 && i.ItemId == 303400),
                "op66 drop refuses socketed skill and other equipped cells the client never drops");
            _playerManager.SetOffline(dropPid);
        }

        const int arrangePid = 920601;
        ResetCharacter(arrangePid);
        _playerStore.GetInventory(arrangePid, () => Enumerable.Range(0, 256)
            .Select(c => new PlayerStore.GenericItem
            {
                InventoryIndex = 0, InventoryCell = (byte)(255 - c), ItemId = 500001, Count = 1
            }).ToList());
        // A dedicated background thread: if Arrange spins it keeps the store lock forever, so the
        // timed join fails the run right here instead of a later test blocking on that lock.
        bool arranged = false;
        var arrange = new Thread(() => arranged = _playerStore.ArrangeMainInventory(arrangePid, out _, out var packed)
            && packed.Count == 256 && packed[^1].InventoryCell == 255) { IsBackground = true };
        arrange.Start();
        Check(arrange.Join(TimeSpan.FromSeconds(5)) && arranged,
            "Arrange finishes on a 256-entry MAIN instead of wrapping its cell counter forever");
    }

    private void SkillSocketIntegrity()
    {
        const int socketPid = 920602;
        ResetCharacter(socketPid);
        _playerStore.GetInventory(socketPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 200199, Count = 1, Skill = 303400 },
            new PlayerStore.GenericItem { InventoryIndex = 6, InventoryCell = 0, ItemId = 303400, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 1, ItemId = 200299, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 303401, Count = 1 }
        ]);
        Check(!_playerStore.TryEquipSkill(socketPid, 1, 0, 0, 0, out _)
            && _playerStore.GetInventory(socketPid, () => [])
                .Count(i => i.ItemId == 303400 && i.InventoryIndex == 6 && i.InventoryCell == 0) == 1
            && !_playerStore.GetInventory(socketPid, () => []).Any(i => i.ItemId == 303400 && i.InventoryIndex == 0),
            "skill equip never moves another part's socketed skill out of its SKILL cell");
        Check(_playerStore.SwapSkillSlots(socketPid, 0, 1)
            && _equipmentRules.ValidateSkill(socketPid, 1, 0, 0, 1, null, null, null, null) == 23
            && _equipmentRules.ValidateSkill(socketPid, 1, 0, 0, 0, null, null, null, null) == 3,
            "skill equip requires the SKILL cell whose nSkillSlot maps to the armor part");
        Check(_playerStore.GetInventory(socketPid, () => [])
                .Single(i => i.InventoryIndex == 6).InventoryCell == 1,
            "skill-slot swap moves the INVEN_SKILL items with the nSkillSlot permutation");
        Check(new UpdateSkillSlot { Result = 0, Slot1 = 0, Slot2 = 1 }.Serialize().SequenceEqual(new byte[] { 76, 0, 0, 1 })
            && new UpdateSkillSlot { Result = 1, Slot1 = 0, Slot2 = 1 }.Serialize().SequenceEqual(new byte[] { 76, 1 }),
            "op76 skill-slot answer carries the swapped button indices the native handler reads on success");
    }

    private void ResetToolTotalsAndRelicRepair()
    {
        const int resetPid = 920603;
        ResetCharacter(resetPid);
        _playerStore.AddItems(resetPid, [(673001, 1), (672001, 1)]);
        _playerStore.GetInventory(resetPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 128403, State = 1, TuneUpOptions = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 2, ItemId = 673001, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 672001, Count = 1 }
        ]);
        Check(_playerStore.TryRemoveItemBind(resetPid, 2, 1, out _)
            && _playerStore.TryResetTuneUp(resetPid, 4, 1, 1, 0, out _, out _) == 0
            && !_playerStore.GetItems(resetPid).Any(x => x.item is 673001 or 672001),
            "bind and tune-up resetters lower the persisted item totals they consume");

        const int relicPid = 920604;
        ResetCharacter(relicPid);
        Check(_itemCatalog.Get(221099) is { ItemGrade: 7, Repairable: true, ItemLimitTimeType: 0 },
            "item catalog keeps Relic helmet 221099 as a repairable grade-7 item");
        _playerStore.GetInventory(relicPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 221099, Count = 1, Durability = 50 }
        ]);
        _playerStore.GetProgress(relicPid).QT = 10_000;
        Check(_playerStore.TryRepair(relicPid, 4, 0, false, 0, out var relicRepaired, out long relicQt)
            && relicRepaired is [{ Durability: 100 }] && relicQt == 10_000,
            "Relic repair costs the 0 QT the client's six-entry ItemGradeParam shows");
    }

    private void MoneyCap()
    {
        const int richPid = 920605;
        ResetCharacter(richPid);
        _playerStore.GetProgress(richPid).Money = PlayerStore.MaxMoney - 100;
        Check(_playerStore.CanReceiveMoney(richPid, 100) && !_playerStore.CanReceiveMoney(richPid, 101),
            "CanReceiveMoney reports whether a credit fits under the int32 CR cap");
        Check(_playerStore.AddMoney(richPid, 1_000) == 100
            && _playerStore.GetProgress(richPid).Money == PlayerStore.MaxMoney
            && _playerStore.AddReward(richPid, 0, 0, 1_000_000).Money == PlayerStore.MaxMoney,
            "AddMoney and AddReward never raise CR past int.MaxValue");
    }
}
