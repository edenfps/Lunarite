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
public static class TuneUpHandler
{
    private static LicenseStore _licenseStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _licenseStore = serviceProvider.GetRequiredService<LicenseStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte cell) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var before = pid > 0
            ? _playerStore.GetInventory(pid, () => []).Where(i => i.InventoryIndex == InventoryType.Main)
                .ToDictionary(i => i.InventoryCell, i => (i.ItemId, i.Count))
            : [];
        byte option = 0; int oldValue = 0, newValue = 0;
        byte result = pid > 0 ? _playerStore.TryTuneUp(pid, cell, _licenseStore.TuneupDiscountPercent(pid), out option, out oldValue, out newValue) : (byte)1;
        bool ok = result == 0;
        client.SendPacket(new TuneUp { Result = result, Option = option });
        if (ok)
        {
            var after = _playerStore.GetInventory(pid, () => []).Where(i => i.InventoryIndex == InventoryType.Main)
                .ToDictionary(i => i.InventoryCell);
            foreach (var (changedCell, old) in before)
            {
                if (changedCell == cell) continue;
                if (after.TryGetValue(changedCell, out var current)
                    && current.ItemId == old.ItemId && current.Count == old.Count) continue;
                client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = changedCell });
                if (current != null) ItemManager.SendInventoryItem(client, current);
            }
            client.SendPacket(new Parameter { Param = Parameter.PARAMETER_MONEY,
                Value = (int)Math.Min(int.MaxValue, _playerStore.GetProgress(pid).Money) });
            client.SendPacket(new UpdateItemTuneUp { Inventory = InventoryType.Main, Cell = cell, TuneUpOptions = newValue });
        }
        return true;
    }
}
