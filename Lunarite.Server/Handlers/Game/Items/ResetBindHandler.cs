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
public static class ResetBindHandler
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
        if (!reader.TryRead(out byte useCell) || !reader.TryRead(out byte targetCell) || reader.RemainingLength != 0)
            return false;
        int pid = Game.PidOf(client);
        int useItemId = pid > 0 ? _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(item => item.InventoryIndex == InventoryType.Main && item.InventoryCell == useCell)?.ItemId ?? 0 : 0;
        PlayerStore.GenericItem? target = null;
        bool ok = false;
        if (pid > 0)
            _multiStoreTransaction.Run(() =>
            {
                ok = _playerStore.TryRemoveItemBind(pid, useCell, targetCell, out target);
                if (ok && useItemId > 0) _itemManager.SynchronizeQuestPickupObjective(pid, useItemId);
            });
        client.SendPacket(new RemoveItemBind { Result = ok ? (byte)0 : (byte)1 });
        if (ok)
        {
            client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = useCell });
            var use = _playerStore.GetInventory(pid, () => [])
                .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == useCell);
            if (use != null) ItemManager.SendInventoryItem(client, use);
            if (target != null) client.SendPacket(new UpdatedItemState
                { Inventory = target.InventoryIndex, Cell = target.InventoryCell, State = target.State });
        }
        return true;
    }
}
