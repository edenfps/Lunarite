using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Packets.Common;

namespace Lunarite.Storage;

// Socket skills follow the player, not the armor (user, 2026-11-12: players lost skills by swapping armor and then
// selling the old piece). In the client a socket skill lives in an armor socket (GenericItem.Skill/Skill1) with its
// item in the SKILL inventory, so retail let it leave with the armor. The server now:
// - on an armor swap, moves each skill of the replaced piece into an empty socket of the same shape on the new piece;
// - returns any skill that does not fit, and every skill of a piece taken off, to a free MAIN cell;
// so armor outside the ARMOR slots holds no skills and selling, dropping, mailing or banking it cannot take one along.
public sealed partial class PlayerStore
{
    // A skill moved out of an armor socket: where its item was and where it is now (both null cells when nothing moved
    // in the inventory: a transfer into the new armor keeps the item in its SKILL cell).
    public sealed record SocketMove(int SkillId, byte? FromSkillCell, byte? ToMainCell, bool Transferred);

    // Call with the item that just left the ARMOR slots (now in MAIN). newArmor is the piece that replaced it, or null
    // for a plain take-off. Socket and skill shapes come from the item catalog (EquipmentRules.ValidateSkill's rule).
    // Returns what moved; the caller refreshes the client.
    public List<SocketMove> ReleaseSockets(int pid, byte oldMainCell, byte? newArmorCell)
    {
        var moves = new List<SocketMove>();
        lock (_lock)
        {
            if (!_inventory.TryGetValue(pid, out var list)) return moves;
            var old = ItemAt(list, InventoryType.Main, oldMainCell);
            if (old == null) return moves;
            var fresh = newArmorCell is byte cell ? ItemAt(list, InventoryType.Armor, cell) : null;
            for (int socket = 0; socket < 2; socket++)
            {
                int skillId = socket == 0 ? old.Skill : old.Skill1;
                if (skillId == 0) continue;
                // Into the new armor: the first empty socket of the same shape.
                if (fresh != null)
                {
                    var shapes = _itemCatalog.Get(fresh.ItemId)?.SkillSocketShapes ?? [];
                    string? shape = _itemCatalog.Get(skillId)?.SkillItemShape;
                    int target = -1;
                    for (int s = 0; s < Math.Min(2, shapes.Count) && target < 0; s++)
                        if ((s == 0 ? fresh.Skill : fresh.Skill1) == 0 && shapes[s] == shape) target = s;
                    if (target >= 0)
                    {
                        if (target == 0) fresh.Skill = skillId; else fresh.Skill1 = skillId;
                        if (socket == 0) old.Skill = 0; else old.Skill1 = 0;
                        moves.Add(new SocketMove(skillId, null, null, true));
                        continue;
                    }
                }
                // Back to the bag: its SKILL item (one no equipped armor still references), else a new item for saves
                // from before skills had one. No free MAIN cell: it stays in the armor, as retail.
                int free = FindEmptyCell(list, MainCapacity(pid));
                if (free < 0) break;
                var skillItem = list.FirstOrDefault(i => i.InventoryIndex == InventoryType.Skill && i.ItemId == skillId
                    && !list.Any(a => a.InventoryIndex == InventoryType.Armor && (a.Skill == skillId || a.Skill1 == skillId)));
                byte? from = skillItem?.InventoryCell;
                if (skillItem == null) { skillItem = CreateAcquiredItem(skillId); list.Add(skillItem); }
                skillItem.InventoryIndex = InventoryType.Main;
                skillItem.InventoryCell = (byte)free;
                if (socket == 0) old.Skill = 0; else old.Skill1 = 0;
                moves.Add(new SocketMove(skillId, from, (byte)free, false));
            }
            if (moves.Count > 0) SaveInventory();
        }
        return moves;
    }

    // One time: armor already in bags or the bank keeps no skills; each comes back to a free MAIN cell (its SKILL item
    // when no equipped armor uses it, else a new item). Returns how many skills were returned.
    public int MigrateLooseArmorSockets()
    {
        int returned = 0;
        lock (_lock)
        {
            foreach (var (pid, list) in _inventory)
            {
                foreach (var armor in list.Where(i => i.InventoryIndex != InventoryType.Armor && (i.Skill != 0 || i.Skill1 != 0)).ToList())
                {
                    for (int socket = 0; socket < 2; socket++)
                    {
                        int skillId = socket == 0 ? armor.Skill : armor.Skill1;
                        if (skillId == 0) continue;
                        int free = FindEmptyCell(list, MainCapacity(pid));
                        if (free < 0) break;
                        var skillItem = list.FirstOrDefault(i => i.InventoryIndex == InventoryType.Skill && i.ItemId == skillId
                            && !list.Any(a => a.InventoryIndex == InventoryType.Armor && (a.Skill == skillId || a.Skill1 == skillId)));
                        if (skillItem == null) { skillItem = CreateAcquiredItem(skillId); list.Add(skillItem); }
                        skillItem.InventoryIndex = InventoryType.Main;
                        skillItem.InventoryCell = (byte)free;
                        if (socket == 0) armor.Skill = 0; else armor.Skill1 = 0;
                        returned++;
                    }
                }
            }
            if (returned != 0) SaveInventory();
        }
        return returned;
    }
}
