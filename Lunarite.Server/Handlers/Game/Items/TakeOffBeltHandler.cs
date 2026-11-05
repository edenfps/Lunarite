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
public static class TakeOffBeltHandler
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
        if (!reader.TryRead(out byte requestedMainCell) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        int itemId = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(item => item.InventoryIndex == InventoryType.Belt && item.InventoryCell == 0)?.ItemId ?? 0;
        bool moved = false; byte mainCell = 0xFF, relocated = 0xFF;
        _multiStoreTransaction.Run(() =>
        {
            moved = _playerStore.TryTakeOff(pid, InventoryType.Belt, 0, requestedMainCell,
                out mainCell, out relocated);
            if (moved && itemId > 0) _itemManager.SynchronizeQuestPickupObjective(pid, itemId);
        });
        client.SendPacket(new TakeOffBelt
        {
            Result = moved ? (byte)0 : (byte)1,
            MainCell = mainCell,
            RelocatedMainCell = relocated
        });
        return true;
    }
}
