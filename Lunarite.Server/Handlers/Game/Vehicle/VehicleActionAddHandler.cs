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
public static class VehicleActionAddHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
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
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte mainCell) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var source = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Main && x.InventoryCell == mainCell);
        byte result = Game.PlayerFor(pid).SummonedVehicle.HasValue ? VehicleResultCode.Alreadyrode :
            _equipmentRules.ValidateVehicle(pid, mainCell, source, _itemCatalog.Get(source?.ItemId ?? 0));
        if (result == 0)
            _multiStoreTransaction.Run(() =>
            {
                if (!_playerStore.SwapSlots(pid, InventoryType.Vehicle, 0, InventoryType.Main, mainCell))
                    result = VehicleResultCode.Usinginven;
                else _itemManager.SynchronizeQuestPickupObjective(pid, source!.ItemId);
            });
        client.SendPacket(new AddVehicle { Result = result, MainCell = mainCell });
        if (result == 0 && _playerStore.TryBindEquipped(pid, InventoryType.Vehicle, 0, out byte vehicleState))
            client.SendPacket(new UpdatedItemState { Inventory = InventoryType.Vehicle, Cell = 0, State = vehicleState });
        return true;
    }
}
