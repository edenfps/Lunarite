using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed partial class ItemManager
{
    public byte[] BuildRosterArmorPreview(int pid)
    {
        var inventory = _playerStore.GetInventory(pid, () => BuildInitialInventory(pid));
        using var writer = new PacketWriter();
        for (byte cell = 0; cell < 5; cell++)
        {
            var item = inventory.FirstOrDefault(x => x.InventoryIndex == InventoryType.Armor && x.InventoryCell == cell);
            if (item == null)
            {
                writer.Write(new byte[32]);
                continue;
            }

            writer.Write(item.ItemId);
            writer.Write(Math.Max(1, item.Count));
            writer.Write(item.InventoryIndex);
            writer.Write(item.InventoryCell);
            writer.Write(item.State);
            writer.Write((byte)0);
            writer.Write(item.LimitTime);
            writer.Write(item.TuneUpOptions);
            writer.Write(item.Skill);
            writer.Write(item.Skill1);
            writer.Write(item.Durability);
        }
        return writer.Buffer;
    }

    public static IReadOnlyList<int> TutorialArmorItemIds() => StartingArmor.Select(x => x.id).ToArray();

    public static GenericItemBase[] InitialArmorPreview()
    {
        var armor = Enumerable.Range(0, 5).Select(_ => new GenericItemBase()).ToArray();
        foreach (var (id, cell, _) in StartingArmor)
            armor[cell] = new GenericItemBase { InventoryCell = cell, ItemID = id, General3 = 100 };
        return armor;
    }

    // Style weapons, starting armor, and any loose earned items moved into MAIN.
    public List<PlayerStore.GenericItem> BuildInitialInventory(int pid)
    {
        byte style = _characterStore.Get(pid)?.Style ?? 0;
        var list = new List<PlayerStore.GenericItem>();
        byte weaponCell = 0;
        foreach (var weaponId in CharacterManager.StyleWeapons(style))
            list.Add(new PlayerStore.GenericItem
            {
                InventoryIndex = InventoryType.Weapon, InventoryCell = weaponCell++, ItemId = weaponId, Durability = 100
            });
        foreach (var (id, armorCell, skill) in StartingArmor)
        {
            list.Add(new PlayerStore.GenericItem
            {
                InventoryIndex = InventoryType.Armor, InventoryCell = armorCell, ItemId = id, Durability = 100,
                Skill = skill
            });
            // Every socketed skill also lives in INVEN_SKILL, in the cell of its armor part.
            if (skill != 0)
                list.Add(new PlayerStore.GenericItem
                {
                    InventoryIndex = InventoryType.Skill, InventoryCell = armorCell, ItemId = skill, Count = 1
                });
        }
        byte cell = 0;
        int capacity = _playerStore.MainCapacity(pid);
        foreach (var (item, num) in _playerStore.GetItems(pid))
        {
            int remaining = num;
            int maxStack = Math.Max(1, _itemCatalog.Get(item)?.MaxCollapseCount ?? num);
            while (remaining > 0 && cell < capacity)
            {
                int count = Math.Min(remaining, maxStack);
                var acquired = _playerStore.CreateAcquiredItem(item, count);
                acquired.InventoryIndex = InventoryType.Main;
                acquired.InventoryCell = cell++;
                list.Add(acquired);
                remaining -= count;
            }
            if (cell >= capacity) break;
        }
        return list;
    }

    public static List<QuestCreate.Item> TutorialLoadoutItems()
    {
        var items = TutorialWeapons.Select((id, cell) => new QuestCreate.Item
        {
            ItemID = id, Count = 1, InvenIndex = InventoryType.Weapon, Cell = (byte)cell, Durability = 100
        }).ToList();
        items.AddRange(StartingArmor.Select(armor => new QuestCreate.Item
        {
            ItemID = armor.id, Count = 1, InvenIndex = InventoryType.Armor, Cell = armor.cell,
            Skill0 = armor.skill, Durability = 100
        }));
        return items;
    }

    internal List<QuestCreate.Item> StartingLoadoutItems(int pid)
    {
        var inventory = _playerStore.GetInventory(pid, () => BuildInitialInventory(pid));
        return inventory
            .Where(i => i.InventoryIndex is InventoryType.Belt or InventoryType.Weapon or InventoryType.Armor)
            .Select(i => new QuestCreate.Item
            {
                ItemID = i.ItemId,
                Count = Math.Max(1, i.Count),
                InvenIndex = i.InventoryIndex,
                Cell = i.InventoryCell,
                State = i.State,
                LimitTime = i.LimitTime,
                TuneUpOptions = i.TuneUpOptions,
                Skill0 = i.Skill,
                Skill1 = i.Skill1,
                Durability = i.WireDurability
            }).ToList();
    }
}
