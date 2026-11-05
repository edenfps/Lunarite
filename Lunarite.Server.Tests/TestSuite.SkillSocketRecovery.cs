using Microsoft.Extensions.DependencyInjection;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Storage;

internal sealed partial class TestSuite
{
    private void SkillSocketRecovery()
    {
        // A skill left in SKILL after its armor was swapped out (and sold or traded) is an orphan:
        // equipping the new armor's skill displaces it to MAIN, as before the socket-dupe fix.
        int orphanPid = _characterStore.Create(939870020, "SocketOrphan", 1, new Custom()).Pid;
        ResetCharacter(orphanPid);
        _characterStore.SetStyle(orphanPid, 3);
        _playerStore.SetRank(orphanPid, 51);
        _playerStore.AddReward(orphanPid, 0, 10_000_000, 0);
        _playerStore.GetInventory(orphanPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 2, ItemId = 222717, Count = 1, Durability = 100 },
            new PlayerStore.GenericItem { InventoryIndex = 6, InventoryCell = 2, ItemId = 332210, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 332211, Count = 1 }
        ]);
        Check(EquipSkillResult(orphanPid, armorCell: 2, mainCell: 1, skillCell: 2) == 0
            && _playerStore.GetInventory(orphanPid, () => []) is var orphanInventory
            && orphanInventory.Any(i => i is { InventoryIndex: 6, InventoryCell: 2, ItemId: 332211 })
            && orphanInventory.Any(i => i is { InventoryIndex: 0, InventoryCell: 1, ItemId: 332210 })
            && orphanInventory.Single(i => i.InventoryIndex == 4).Skill == 332211,
            "skill equip displaces an orphaned SKILL occupant no equipped armor references to MAIN");

        // An occupant some equipped armor still sockets is never moved: native SKILL_SLOT_INVALID.
        int lockedPid = _characterStore.Create(939870021, "SocketLocked", 1, new Custom()).Pid;
        ResetCharacter(lockedPid);
        _characterStore.SetStyle(lockedPid, 3);
        _playerStore.SetRank(lockedPid, 51);
        _playerStore.AddReward(lockedPid, 0, 10_000_000, 0);
        _playerStore.GetInventory(lockedPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 221099, Count = 1, Durability = 100, Skill = 331001 },
            new PlayerStore.GenericItem { InventoryIndex = 6, InventoryCell = 2, ItemId = 331001, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 2, ItemId = 222717, Count = 1, Durability = 100 },
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 1, ItemId = 332211, Count = 1 }
        ]);
        Check(EquipSkillResult(lockedPid, armorCell: 2, mainCell: 1, skillCell: 2) == ItemResultCode.SkillSlotInvalid
            && _playerStore.GetInventory(lockedPid, () => []) is var lockedInventory
            && lockedInventory.Any(i => i is { InventoryIndex: 6, InventoryCell: 2, ItemId: 331001 })
            && lockedInventory.Any(i => i is { InventoryIndex: 0, InventoryCell: 1, ItemId: 332211 }),
            "skill equip answers SKILL_SLOT_INVALID when the SKILL cell holds an equipped armor's skill");

        // Saves from before skill-slot swaps moved INVEN_SKILL are realigned when the store loads.
        const int alignPid = 920606;
        ResetCharacter(alignPid);
        _playerStore.GetInventory(alignPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 0, ItemId = 221099, Count = 1, Skill = 331001 },
            new PlayerStore.GenericItem { InventoryIndex = 4, InventoryCell = 1, ItemId = 222100, Count = 1, Skill = 331101 },
            new PlayerStore.GenericItem { InventoryIndex = 6, InventoryCell = 0, ItemId = 331001, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 6, InventoryCell = 1, ItemId = 331101, Count = 1 },
            new PlayerStore.GenericItem { InventoryIndex = 6, InventoryCell = 3, ItemId = 334001, Count = 1 }
        ]);
        _playerStore.GetProgress(alignPid).SkillSlot = [1, 0, 2, 3, 4];
        _playerStore.AddMoney(alignPid, 1);
        var reloaded = ActivatorUtilities.CreateInstance<PlayerStore>(_services);
        var aligned = reloaded.GetInventory(alignPid, () => []).Where(i => i.InventoryIndex == 6).ToList();
        Check(aligned.Single(i => i.ItemId == 331001).InventoryCell == 1
            && aligned.Single(i => i.ItemId == 331101).InventoryCell == 0
            && aligned.Single(i => i.ItemId == 334001).InventoryCell == 3,
            "loading realigns socketed skills with nSkillSlot and leaves orphaned skills in place");
    }

    private byte EquipSkillResult(int pid, byte armorCell, byte mainCell, byte skillCell)
    {
        using var link = OpenSocialGameSocket(pid);
        _playerManager.SetOnline(pid, link.Client);
        link.Client.Player = _playerManager.Get(pid);
        try
        {
            using var request = new PacketWriter();
            request.Write((byte)73); request.Write(armorCell); request.Write((byte)0);
            request.Write(mainCell); request.Write(skillCell);
            Game.OnHandlePacket(link.Client, request.Buffer);
            byte result = 0xFF;
            ReceivesSocial(link, p => p[0] == 74 && (result = p[1]) == p[1]);
            return result;
        }
        finally { _playerManager.SetOffline(pid); }
    }
}
