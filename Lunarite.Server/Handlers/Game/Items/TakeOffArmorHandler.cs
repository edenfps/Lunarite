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
public static class TakeOffArmorHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static CityManager _cityManager = null!;
    private static ItemManager _itemManager = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte armorCell) || !reader.TryRead(out byte requestedMainCell)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        int itemIdBefore = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(item => item.InventoryIndex == InventoryType.Armor && item.InventoryCell == armorCell)?.ItemId ?? 0;
        bool moved = false; byte mainCell = 0xFF;
        _multiStoreTransaction.Run(() =>
        {
            moved = _playerStore.TryTakeOff(pid, InventoryType.Armor, armorCell, requestedMainCell,
                out mainCell, out _);
            if (moved && itemIdBefore > 0) _itemManager.SynchronizeQuestPickupObjective(pid, itemIdBefore);
        });
        var item = moved
            ? _playerStore.GetInventory(pid, () => [])
                .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == mainCell)
            : null;
        client.SendPacket(new TakeOffArmor
        {
            Result = item != null ? (byte)0 : (byte)1,
            ArmorCell = armorCell,
            MainCell = mainCell,
            ItemID = item?.ItemId ?? 0,
            Count = item?.Count ?? 0,
            State = item?.State ?? 0,
            LimitTime = item?.LimitTime ?? 0,
            TuneUpOptions = item?.TuneUpOptions ?? 0,
            Skill0 = item?.Skill ?? 0,
            Skill1 = item?.Skill1 ?? 0,
            Durability = item?.Durability ?? 0
        });
        if (item != null)
        {
            _cityManager.BroadcastPresence(client, new EquipArmorOther { Pid = pid, Part = armorCell, ItemId = 0 });
            _cityManager.BroadcastPresence(client, new QuestSubItem { Pid = pid, Inventory = InventoryType.Armor, Cell = armorCell });
        }
        return true;
    }
}
