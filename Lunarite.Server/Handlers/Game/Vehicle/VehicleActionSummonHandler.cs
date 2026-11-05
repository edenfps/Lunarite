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
public static class VehicleActionSummonHandler
{
    private static ItemCatalog _itemCatalog = null!;
    private static CityManager _cityManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(VehicleActionSummonHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _itemCatalog = serviceProvider.GetRequiredService<ItemCatalog>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var equippedItem = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Vehicle && x.InventoryCell == 0
            && _itemCatalog.Get(x.ItemId)?.Vehicle == true);
        bool equipped = equippedItem != null;
        client.SendPacket(new SummonVehicle { Result = equipped ? (byte)0 : (byte)6 });
        if (equipped)
        {
            // SummonVehicle is a ride/dismount toggle. OnUpdateVehicle's nVehicleIndex is the
            // currently ridden vehicle, so observers get zero on dismount.
            int vehicleItemId;
            if (Game.PlayerFor(pid).TryClearSummonedVehicle(out _)) vehicleItemId = 0;
            else
            {
                vehicleItemId = equippedItem!.ItemId;
                Game.PlayerFor(pid).SummonedVehicle = vehicleItemId;
            }
            _logger.LogInformation("[Vehicle] PID {0} summon toggle: equipped {1}, mounted {2}, city map {3}.",
                pid, equippedItem!.ItemId, vehicleItemId, (Game.PlayerFor(pid).CityMap ?? 0));
            _cityManager.BroadcastPresence(client, new UpdateVehicle { Pid = pid, VehicleItemId = vehicleItemId });
        }
        return true;
    }
}
