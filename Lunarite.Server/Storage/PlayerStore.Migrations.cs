using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Packets.Common;

namespace Lunarite.Storage;

// Startup repairs for persisted data written by earlier server versions.
public sealed partial class PlayerStore
{
    // HuxleyMultiGame.LoadItemDataFromIni socket-equips only tutorial skill 303400 (Sprint).
    // Only the exact obsolete armor/skill pairs are rewritten; player-chosen skills are kept. An empty
    // socket on the starter boots is left alone: the player took the skill off, and refilling it
    // would duplicate the skill on every inventory read.
    private static bool RepairLegacyStarterSkills(List<GenericItem> list)
    {
        bool migrated = false;
        foreach (var item in list.Where(i => i.InventoryIndex == InventoryType.Armor))
        {
            int corrected = (item.ItemId, item.Skill) switch
            {
                (200199, 332151) => 0,
                (200299, 312203) => 0,
                (200399, 321003) => 0,
                _ => item.Skill
            };
            if (corrected == item.Skill) continue;
            item.Skill = corrected;
            migrated = true;
        }
        return migrated;
    }

    public void MigrateLegacyStarterSkills()
    {
        lock (_lock)
        {
            int count = 0;
            foreach (var (pid, list) in _inventory)
            {
                if (!RepairLegacyStarterSkills(list)) continue;
                count++;
                _logger.LogInformation("[Store] migrated PID {0} to the retail tutorial socket-skill loadout.", pid);
            }
            if (count != 0) SaveInventory();
            _logger.LogInformation("[Store] tutorial socket-skill migration checked {0} saved character(s); repaired {1}.",
                _inventory.Count, count);
        }
    }

    // Equipped durability above 100 marks a character whose quest results were decoded from
    // displaced bytes; only those characters are reset, so genuinely broken gear elsewhere survives.
    public int MigrateCorruptMissionDurability()
    {
        lock (_lock)
        {
            bool forceBridgeRepair = Environment.GetEnvironmentVariable("REPAIR_TYPE4_DURABILITY_ONCE") == "1";
bool IsEquipped(GenericItem i) =>
                i.InventoryIndex == InventoryType.Weapon || i.InventoryIndex == InventoryType.Armor;
            bool NeedsRepair(GenericItem i) =>
                (forceBridgeRepair && IsEquipped(i) && i.Durability != MaxDurability)
                || i.Durability > MaxDurability
                || (IsEquipped(i) && i.Durability <= 0);

            int repaired = 0;
            foreach (var (pid, list) in _inventory)
            {
                bool contaminated = forceBridgeRepair || list.Any(i => IsEquipped(i) && i.Durability > MaxDurability);
                if (!contaminated) continue;

                int before = repaired;
                foreach (var item in list.Where(NeedsRepair))
                {
                    item.Durability = MaxDurability;
                    repaired++;
                }
                if (repaired != before)
                    _logger.LogInformation("[Store] repaired {0} pre-fix mission durability value(s) for PID {1}.",
                        repaired - before, pid);
            }
            if (repaired != 0) SaveInventory();
            if (forceBridgeRepair)
                _logger.LogWarning("[Store] one-time Type-4 bridge repair reset all saved equipped durability to 100.");
            _logger.LogInformation("[Store] corrupt mission-durability migration repaired {0} value(s).", repaired);
            return repaired;
        }
    }
}
