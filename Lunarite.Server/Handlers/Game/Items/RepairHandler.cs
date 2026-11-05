using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class RepairHandler
{
    private static CityManager _cityManager = null!;
    private static LicenseStore _licenseStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _licenseStore = serviceProvider.GetRequiredService<LicenseStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte inventory) || !reader.TryRead(out byte repairType)) return false;
        int pid = Game.PidOf(client);
        bool success;
        long qt;
        List<PlayerStore.GenericItem> repaired;
        byte? responseCell = null;
        byte responseInventory;
        if (inventory == 7)
        {
            if (reader.RemainingLength != 0 || repairType > 1) return false;
            bool equippedOnly = repairType == 1;
            success = _playerStore.TryRepair(pid, null, null, equippedOnly, _licenseStore.RepairDiscountPercent(pid), out repaired, out qt);
            responseInventory = equippedOnly ? byte.MaxValue : (byte)7;
        }
        else
        {
            if (!reader.TryRead(out byte cell) || reader.RemainingLength != 0 || repairType != 0) return false;
            success = _playerStore.TryRepair(pid, inventory, cell, false, _licenseStore.RepairDiscountPercent(pid), out repaired, out qt);
            responseInventory = inventory;
            responseCell = cell;
        }
        if (success)
        {
            client.SendPacket(new Parameter { Param = Parameter.PARAMETER_QT,
                Value = (int)Math.Min(int.MaxValue, qt) });
            client.SendPacket(new UpdateRepair { Inventory = responseInventory, Cell = responseCell });
            // ProcUpdateArmorOther (server op80) takes the full 32-byte item, so nearby pawns
            // refresh durability and sockets in place.
            foreach (var armor in repaired.Where(item => item.InventoryIndex == InventoryType.Armor))
                _cityManager.BroadcastPresence(client, new UpdateArmorOther
                    { Pid = pid, Part = armor.InventoryCell, Item = armor });
        }
        else client.SendPacket(new UpdateRepair { Inventory = 8 });
        return true;
    }
}
