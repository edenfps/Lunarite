using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ItemArrangeHandler
{
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        bool arranged = _playerStore.ArrangeMainInventory(pid, out var oldCells, out var items);
        if (arranged)
        {
            foreach (byte cell in oldCells)
                client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = cell });
            foreach (var item in items) ItemManager.SendInventoryItem(client, item);
            client.SendPacket(new UpdateInventoryViews { View = UpdateInventoryViews.Inventory });
        }
        client.SendPacket(new ItemArrange { Result = arranged ? (byte)0 : (byte)1 });
        return true;
    }
}
