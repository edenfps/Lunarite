using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SwapItemInSameInvenHandler
{
    private static EquipmentRules _equipmentRules = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _equipmentRules = serviceProvider.GetRequiredService<EquipmentRules>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte inven) || !reader.TryRead(out byte sourceCell)
            || !reader.TryRead(out byte targetCell) || !reader.TryRead(out ushort count)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        bool weaponSlotsOpen = inven != InventoryType.Weapon
            || (_equipmentRules.IsOpenedWeaponSlot(pid, sourceCell)
                && _equipmentRules.IsOpenedWeaponSlot(pid, targetCell));
        bool refresh = false;
        PlayerStore.GenericItem? sourceAfter = null, targetAfter = null;
        bool moved = weaponSlotsOpen && _playerStore.TryMoveWithinInventory(pid, inven, sourceCell, targetCell, count,
            out refresh, out sourceAfter, out targetAfter);
        if (!moved)
        {
            client.SendPacket(new SwapItemInSameInven { Result = ItemResultCode.Error });
            return true;
        }

        if (refresh)
        {
            client.SendPacket(new RemoveItem { InventoryIndex = inven, Cell = sourceCell });
            client.SendPacket(new RemoveItem { InventoryIndex = inven, Cell = targetCell });
            if (sourceAfter != null) ItemManager.SendInventoryItem(client, sourceAfter);
            if (targetAfter != null) ItemManager.SendInventoryItem(client, targetAfter);
        }
        client.SendPacket(new SwapItemInSameInven
        {
            Result = refresh ? (byte)31 : (byte)0,
            Inventory = inven,
            SourceCell = sourceCell,
            TargetCell = targetCell
        });
        return true;
    }
}
