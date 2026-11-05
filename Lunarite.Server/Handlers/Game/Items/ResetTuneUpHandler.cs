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
public static class ResetTuneUpHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte useCell) || !reader.TryRead(out byte targetCell) ||
            !reader.TryRead(out byte resetType) || !reader.TryRead(out byte option) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        int value = 0;
        int useItemId = pid > 0 ? _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(item => item.InventoryIndex == InventoryType.Main && item.InventoryCell == useCell)?.ItemId ?? 0 : 0;
        byte result = 1;
        if (pid > 0)
            _multiStoreTransaction.Run(() =>
            {
                result = _playerStore.TryResetTuneUp(pid, useCell, targetCell, resetType, option, out _, out value);
                if (result == 0 && useItemId > 0) _itemManager.SynchronizeQuestPickupObjective(pid, useItemId);
            });
        bool ok = result == 0;
        client.SendPacket(new ResetTuneUp { Result = result, ResetType = resetType, Option = option });
        if (ok)
        {
            client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = useCell });
            var use = _playerStore.GetInventory(pid, () => [])
                .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == useCell);
            if (use != null) ItemManager.SendInventoryItem(client, use);
            client.SendPacket(new UpdateItemTuneUp { Inventory = InventoryType.Main, Cell = targetCell, TuneUpOptions = value });
        }
        return true;
    }
}
