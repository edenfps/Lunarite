using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class EquipSkillHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemCatalog _itemCatalog = null!;
    private static CityManager _cityManager = null!;
    private static ItemManager _itemManager = null!;
    private static EquipmentRules _equipmentRules = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _equipmentRules = serviceProvider.GetRequiredService<EquipmentRules>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _itemCatalog = serviceProvider.GetRequiredService<ItemCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte armorCell) || !reader.TryRead(out byte socket)
            || !reader.TryRead(out byte mainCell) || !reader.TryRead(out byte skillCell)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var inventory = _playerStore.GetInventory(pid, () => []);
        var equippedArmor = inventory
            .FirstOrDefault(i => i.InventoryIndex == InventoryType.Armor && i.InventoryCell == armorCell);
        var source = inventory.FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == mainCell);
        int replacedItemId = inventory
            .FirstOrDefault(i => i.InventoryIndex == InventoryType.Skill && i.InventoryCell == skillCell)?.ItemId ?? 0;
        byte result = _equipmentRules.ValidateSkill(pid, armorCell, socket, mainCell, skillCell,
            equippedArmor, source, _itemCatalog.Get(equippedArmor?.ItemId ?? 0), _itemCatalog.Get(source?.ItemId ?? 0));
        PlayerStore.GenericItem? armor = null;
        bool moved = false;
        if (result == 0)
            _multiStoreTransaction.Run(() =>
            {
                moved = _playerStore.TryEquipSkill(pid, armorCell, socket, mainCell, skillCell, out armor);
                if (moved) _itemManager.SynchronizeQuestPickupObjectives(pid, [source!.ItemId, replacedItemId]);
            });
        if (result == 0 && !moved) result = ItemResultCode.Error;
        client.SendPacket(new EquipSkill
        {
            Result = result, SkillCell = skillCell, MainCell = mainCell,
            Armor = armor
        });
        if (moved && armor != null)
        {
            if (_playerStore.TryBindEquipped(pid, InventoryType.Skill, skillCell, out byte skillState))
                client.SendPacket(new UpdatedItemState { Inventory = InventoryType.Skill, Cell = skillCell, State = skillState });
            _cityManager.BroadcastPresence(client, new UpdateArmorOther { Pid = pid, Part = armorCell, Item = armor });
            _cityManager.BroadcastPresence(client, new QuestUpdateItem { Pid = pid, Item = armor });
        }
        return true;
    }
}
