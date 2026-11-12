using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TakeOffSkillHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static CityManager _cityManager = null!;
    private static ItemManager _itemManager = null!;
    private static PlayerStore _playerStore = null!;
    private static WardrobeManager _wardrobeManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _wardrobeManager = serviceProvider.GetRequiredService<WardrobeManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte armorCell) || !reader.TryRead(out byte socket)
            || !reader.TryRead(out byte requestedMainCell) || !reader.TryRead(out byte requestedSkillCell)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var equippedArmor = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(item => item.InventoryIndex == InventoryType.Armor && item.InventoryCell == armorCell);
        int itemId = equippedArmor == null ? 0 : socket == 0 ? equippedArmor.Skill : equippedArmor.Skill1;
        bool moved = false; byte mainCell = 0xFF, skillCell = 0xFF;
        PlayerStore.GenericItem? armor = null;
        _multiStoreTransaction.Run(() =>
        {
            moved = _playerStore.TryTakeOffSkill(pid, armorCell, socket, requestedMainCell, requestedSkillCell,
                out mainCell, out skillCell, out armor);
            if (moved && itemId > 0) _itemManager.SynchronizeQuestPickupObjective(pid, itemId);
        });
        client.SendPacket(new TakeOffSkill
        {
            Result = moved ? (byte)0 : (byte)1, SkillCell = skillCell, MainCell = mainCell,
            Armor = armor
        });
        if (moved && armor != null)
        {
            _cityManager.BroadcastPresence(client, _wardrobeManager.ArmorOther(pid, armorCell, armor));
            _cityManager.BroadcastPresence(client, new QuestUpdateItem { Pid = pid, Item = armor });
        }
        return true;
    }
}
