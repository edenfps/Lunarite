using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// Backend op4 `dbbd` (SendBattleUseItem, 0x111C7020): a player used `count` of the item at
// (inventory, cell) during a battle. The dedicated server already took it from its own copy; the
// account's inventory follows.
[PacketHandler]
public static class BattleBackendUseItemHandler
{
    private static BattleServerManager _battleServerManager = null!;
    private static PlayerStore _playerStore = null!;
    private static ItemManager _itemManager = null!;
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(BattleBackendUseItemHandler));
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int pid) || !reader.TryRead(out byte inventory) || !reader.TryRead(out byte cell)
            || !reader.TryRead(out int count) || reader.RemainingLength != 0
            || !_battleServerManager.IsServerLink(client.Id))
            return false;
        if (!_battleServerManager.IsLinkPlayer(client.Id, pid))
        {
            _logger.LogWarning("[BattleBackend] rejected item use for PID {0}: not a player of this battle server.", pid);
            return true;
        }
        bool used = false;
        List<PlayerStore.GenericItem> before = [], after = [];
        int itemId = 0;
        _multiStoreTransaction.Run(() =>
        {
            before = _playerStore.GetInventorySnapshot(pid, () => _itemManager.BuildInitialInventory(pid));
            itemId = before.FirstOrDefault(x => x.InventoryIndex == inventory && x.InventoryCell == cell)?.ItemId ?? 0;
            if (!_playerStore.TryDropItem(pid, inventory, cell, count, out _)) return;
            if (itemId > 0) _itemManager.SynchronizeQuestPickupObjective(pid, itemId);
            after = _playerStore.GetInventorySnapshot(pid, () => _itemManager.BuildInitialInventory(pid));
            used = true;
        });
        if (used && Game.TryGetClient(pid, out var playerClient))
            foreach (var change in ItemManager.BuildInventorySlotChanges(before, after, inventory))
                playerClient.SendPacket(change);
        _logger.LogInformation("[BattleBackend] PID {0} used item {1} x{2} at {3}/{4} in battle: applied={5}.",
            pid, itemId, count, inventory, cell, used);
        return true;
    }
}
