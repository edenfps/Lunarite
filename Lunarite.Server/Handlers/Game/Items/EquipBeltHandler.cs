using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class EquipBeltHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemCatalog _itemCatalog = null!;
    private static ItemManager _itemManager = null!;
    private static ILogger _logger = null!;
    private static EquipmentRules _equipmentRules = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(EquipBeltHandler));

        _equipmentRules = serviceProvider.GetRequiredService<EquipmentRules>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _itemCatalog = serviceProvider.GetRequiredService<ItemCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte mainCell) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var source = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Main && x.InventoryCell == mainCell);
        int replacedItemId = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Belt && x.InventoryCell == 0)?.ItemId ?? 0;
        var data = source == null ? null : _itemCatalog.Get(source.ItemId);
        byte result = _equipmentRules.ValidateBelt(pid, mainCell, source, data);
        if (result == 0)
            _multiStoreTransaction.Run(() =>
            {
                result = _playerStore.SwapSlots(pid, InventoryType.Belt, 0, InventoryType.Main, mainCell)
                    ? (byte)0 : (byte)1;
                if (result == 0) _itemManager.SynchronizeQuestPickupObjectives(pid, [source!.ItemId, replacedItemId]);
            });
        bool moved = result == 0;
        client.SendPacket(new EquipBelt { Result = result, MainCell = mainCell });
        if (moved && _playerStore.TryBindEquipped(pid, InventoryType.Belt, 0, out byte beltState))
            client.SendPacket(new UpdatedItemState { Inventory = InventoryType.Belt, Cell = 0, State = beltState });
        if (moved && _playerStore.TryStartEquippedLimit(pid, InventoryType.Belt, 0, out int beltLimit))
            client.SendPacket(new ItemLimitTimeUpdate { Inventory = InventoryType.Belt, Cell = 0, LimitTime = beltLimit });
        _logger.LogInformation("[Items] PID {0} belt equip MAIN[{1}] -> BELT[0], result={2}.",
            pid, mainCell, result);
        return true;
    }
}
