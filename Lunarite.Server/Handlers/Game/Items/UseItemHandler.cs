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

// op77 SendUseItem: one consumable from a MAIN cell. The item class picks the effect; the answer's
// use type tells the client which callback to run (ENUM_RESULTTYPE_USEITEM).
[PacketHandler]
public static class UseItemHandler
{
    private static ItemCatalog _itemCatalog = null!;
    private static ItemManager _itemManager = null!;
    private static EquipmentRules _equipmentRules = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _equipmentRules = serviceProvider.GetRequiredService<EquipmentRules>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _itemCatalog = serviceProvider.GetRequiredService<ItemCatalog>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte mainCell) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var item = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == mainCell);
        var data = _itemCatalog.Get(item?.ItemId ?? 0);
        byte result = _equipmentRules.ValidateUseItem(pid, mainCell, item, data);
        if (result != 0)
        {
            client.SendPacket(new UseItem { Result = result, UseType = UseItemResultCode.Craftrecipe });
            return true;
        }

        string itemClass = data!.ClassName;
        var outcome =
            itemClass.Contains("HuxleyCityItemData_EnlargeInventory", StringComparison.Ordinal)
                ? _itemManager.UseExpansionItem(client, pid, mainCell, item!, data, bank: false)
            : itemClass.Contains("HuxleyCityItemData_EnlargeBank", StringComparison.Ordinal)
                ? _itemManager.UseExpansionItem(client, pid, mainCell, item!, data, bank: true)
            : itemClass.Contains("HuxleyCityItemData_ItemColorSpray", StringComparison.Ordinal)
                ? _itemManager.UseColorSpray(client, pid, mainCell, item!)
            : itemClass.Contains("HuxleyCityItemData_HPointCoupon", StringComparison.Ordinal)
                ? _itemManager.UseHPointCoupon(client, pid, mainCell, item!, data)
            : itemClass.Contains("HuxleyCityItemData_PersonalRespawn", StringComparison.Ordinal)
                ? _itemManager.UsePersonalRespawn(client, pid, mainCell, item!, data)
            : itemClass.Contains("HuxleyCityItemData_ResetBattleRecord", StringComparison.Ordinal)
                ? _itemManager.UseBattleRecordReset(client, pid, mainCell, item!)
            : data.CityItemDuration > 0 && data.CityItemGroup > 0
                ? _itemManager.UseDurativeItem(client, pid, mainCell, item!, data)
            : _itemManager.LearnRecipe(client, pid, mainCell, item!);
        client.SendPacket(new UseItem
            { Result = outcome.Result, UseType = outcome.UseType, ItemData = outcome.ItemData });
        return true;
    }
}
