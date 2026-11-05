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
public static class ClanContributionHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static ClanStore _clanStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int battleMedalId) || !reader.TryRead(out int battleCount)
            || !reader.TryRead(out int guardMedalId) || !reader.TryRead(out int guardCount)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte result = 0;
        List<byte> changedCells = [];
        _multiStoreTransaction.Run(() =>
        {
            result = _clanStore.Contribute(pid, battleMedalId,
                battleCount, guardMedalId, guardCount, out changedCells);
            if (result == 40)
                _itemManager.SynchronizeQuestPickupObjectives(pid, [battleMedalId, guardMedalId]);
        });
        if (result == 40)
            foreach (byte cell in changedCells.Distinct())
            {
                client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = cell });
                var remaining = _playerStore.GetInventory(pid, () => [])
                    .FirstOrDefault(x => x.InventoryIndex == InventoryType.Main && x.InventoryCell == cell);
                if (remaining != null) ItemManager.SendInventoryItem(client, remaining);
            }
        client.SendPacket(new ClanActionResult { Opcode = 193,
            Result = result });
        return true;
    }
}
