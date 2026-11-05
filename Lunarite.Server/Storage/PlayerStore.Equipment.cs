using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    // Swaps (invenA, cellA) with (invenB, cellB); equipping is a MAIN <-> WEAPON/ARMOR swap.
    public bool SwapSlots(int pid, byte invenA, byte cellA, byte invenB, byte cellB)
    {
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var a = ItemAt(list, invenA, cellA);
            var b = ItemAt(list, invenB, cellB);
            if (b == null) return false;
            if (a != null) { a.InventoryIndex = invenB; a.InventoryCell = cellB; }
            b.InventoryIndex = invenA; b.InventoryCell = cellA;
            SaveInventory();
            return true;
        }
    }

    // Moves one equipped item into MAIN. If the requested MAIN cell is occupied, its item is
    // relocated to the first free cell (reported back to the client); 0xFF picks a free cell.
    public bool TryTakeOff(int pid, byte equippedInven, byte equippedCell, byte requestedMainCell,
        out byte mainCell, out byte relocatedMainCell)
    {
        mainCell = requestedMainCell;
        relocatedMainCell = 0xFF;

        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var equipped = ItemAt(list, equippedInven, equippedCell);
            if (equipped == null) return false;

            if (mainCell == 0xFF)
            {
                int free = FindEmptyCell(list, MainCapacity(pid));
                if (free < 0) return false;
                mainCell = (byte)free;
            }
            else if (mainCell >= MainCapacity(pid))
            {
                return false;
            }

            var occupying = ItemAt(list, InventoryType.Main, mainCell);
            if (occupying != null)
            {
                int free = FindEmptyCell(list, MainCapacity(pid));
                if (free < 0) return false;
                relocatedMainCell = (byte)free;
                occupying.InventoryCell = relocatedMainCell;
            }

            equipped.InventoryIndex = InventoryType.Main;
            equipped.InventoryCell = mainCell;
            SaveInventory();
            return true;
        }
    }

    public bool TryEquipSkill(int pid, byte armorCell, byte socket, byte mainCell, byte skillCell,
        out GenericItem? armorAfter)
    {
        armorAfter = null;
        if (armorCell >= 5 || socket >= 2 || mainCell >= MainCapacity(pid) || skillCell >= 5) return false;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var armor = ItemAt(list, InventoryType.Armor, armorCell);
            var source = ItemAt(list, InventoryType.Main, mainCell);
            if (armor == null || source == null || source.ItemId / 100000 != 3) return false;
            // Only into an empty socket. A SKILL cell occupant that an equipped armor still
            // socketed stays put (moving it to MAIN would duplicate that skill); an orphan whose
            // armor left the ARMOR slots is displaced to MAIN as before.
            if ((socket == 0 ? armor.Skill : armor.Skill1) != 0 || IsSocketedSkillCell(list, skillCell)) return false;

            var orphan = ItemAt(list, InventoryType.Skill, skillCell);
            if (orphan != null) { orphan.InventoryIndex = InventoryType.Main; orphan.InventoryCell = mainCell; }
            source.InventoryIndex = InventoryType.Skill; source.InventoryCell = skillCell;
            if (socket == 0) armor.Skill = source.ItemId; else armor.Skill1 = source.ItemId;
            armorAfter = Clone(armor);
            SaveInventory();
            return true;
        }
    }

    // True when SKILL[skillCell] holds a skill that an armor in the ARMOR slots still references.
    public bool IsSocketedSkillCell(int pid, byte skillCell)
    {
        lock (_lock) return _inventory.TryGetValue(pid, out var list) && IsSocketedSkillCell(list, skillCell);
    }

    // Callers must hold _lock.
    private static bool IsSocketedSkillCell(List<GenericItem> list, byte skillCell)
    {
        var occupant = ItemAt(list, InventoryType.Skill, skillCell);
        return occupant != null && list.Any(i => i.InventoryIndex == InventoryType.Armor
            && (i.Skill == occupant.ItemId || i.Skill1 == occupant.ItemId));
    }

    public bool TryTakeOffSkill(int pid, byte armorCell, byte socket, byte requestedMainCell,
        byte requestedSkillCell, out byte mainCell, out byte skillCell, out GenericItem? armorAfter)
    {
        mainCell = requestedMainCell;
        skillCell = requestedSkillCell;
        armorAfter = null;
        if (armorCell >= 5 || socket >= 2) return false;
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return false;
            var armor = ItemAt(list, InventoryType.Armor, armorCell);
            if (armor == null) return false;
            int skillId = socket == 0 ? armor.Skill : armor.Skill1;
            if (skillId == 0) return false;

            GenericItem? skillItem;
            if (skillCell == 0xFF)
            {
                skillItem = list.FirstOrDefault(i => i.InventoryIndex == InventoryType.Skill && i.ItemId == skillId);
                skillCell = skillItem?.InventoryCell ?? armorCell;
            }
            else
            {
                if (skillCell >= 5) return false;
                byte cell = skillCell;
                skillItem = list.FirstOrDefault(i =>
                    i.InventoryIndex == InventoryType.Skill && i.InventoryCell == cell && i.ItemId == skillId);
            }

            if (mainCell == 0xFF)
            {
                int free = FindEmptyCell(list, MainCapacity(pid));
                if (free < 0) return false;
                mainCell = (byte)free;
            }
            else if (mainCell >= MainCapacity(pid) || ItemAt(list, InventoryType.Main, mainCell) != null)
            {
                return false;
            }

            // A skill that came with the armor (saves from before starter skills had a SKILL item).
            if (skillItem == null) { skillItem = CreateAcquiredItem(skillId); list.Add(skillItem); }
            skillItem.InventoryIndex = InventoryType.Main; skillItem.InventoryCell = mainCell;
            if (socket == 0) armor.Skill = 0; else armor.Skill1 = 0;
            armorAfter = Clone(armor);
            SaveInventory();
            return true;
        }
    }

    // Applies the one-time style loadout. A weapon already in its target slot keeps its full
    // ownership record; new ones go through the normal acquisition rules.
    public List<byte> ReplaceInventorySlots(int pid, byte inven, int[] ids)
    {
        lock (_lock)
        {
            var cleared = new List<byte>();
            if (_inventory.TryGetValue(pid, out var list))
            {
                var previous = list.Where(i => i.InventoryIndex == inven).ToDictionary(i => i.InventoryCell);
                cleared = previous.Keys.ToList();
                list.RemoveAll(i => i.InventoryIndex == inven);
                for (byte cell = 0; cell < ids.Length; cell++)
                {
                    var item = previous.TryGetValue(cell, out var existing) && existing.ItemId == ids[cell]
                        ? Clone(existing) : CreateAcquiredItem(ids[cell]);
                    item.InventoryIndex = inven;
                    item.InventoryCell = cell;
                    list.Add(item);
                }
                SaveInventory();
            }
            return cleared;
        }
    }
}
