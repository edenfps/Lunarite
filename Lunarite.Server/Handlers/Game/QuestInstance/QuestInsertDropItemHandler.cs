using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// Single quest-pickup inventory transaction. The native client sends op47 `dd`; server op50
// echoes result/item/count and releases ITEM_TRANSACTION_INSERTDROPITEM_QUEST.
[PacketHandler]
public static class QuestInsertDropItemHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ItemManager _itemManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestInsertDropItemHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestItemRequest.TryDeserialize(ref reader, out var packet))
            return false;

        int pid = Game.PidOf(client);
        List<PlayerStore.GenericItem> inserted = [];
        List<(int ItemId, int Entitled, int Inserted)> claims = [];
        bool valid = false;
        // The mission-end screen can report after the player left the instance (see TryResolveLootClaim).
        if (_questInstanceManager.TryResolveLootClaim(client.Id, pid, out int instanceToken, out int poolPid))
            valid = QuestLootGrants.TryClaimUpTo(instanceToken, poolPid,
                [(packet.ItemIndex, packet.Quantity)], clamp: false, entitled =>
            {
                int[]? placed = null;
                _multiStoreTransaction.Run(() =>
                {
                    if (!_playerStore.TryAddQuestItemsUpTo(pid, entitled, out inserted, out var counts)) return;
                    if (counts[0] > 0) _itemManager.SynchronizeQuestPickupObjective(pid, packet.ItemIndex);
                    placed = counts;
                });
                return placed;
            }, out claims);
        if (valid)
        {
            foreach (var item in inserted) ItemManager.SendInventoryItem(client, item);
        }
        // Short of room: INSERT_PROTION with the quantity actually inserted (possibly 0). The client
        // subtracts it from the panel entry and shows NotEnoghSlot; the rest stays claimable.
        int insertedCount = valid ? claims.Sum(x => x.Inserted) : 0;
        byte answer = !valid ? (byte)1 : insertedCount == packet.Quantity ? (byte)0 : ItemResultCode.InsertProtion;
        client.SendPacket(new InsertDropItemQuest
        {
            Answer = answer,
            ItemIndex = packet.ItemIndex,
            Quantity = answer == ItemResultCode.InsertProtion ? insertedCount : packet.Quantity
        });
        _logger.LogInformation("[Quest] op47 single drop PID {0}: {1}x{2}, result={3}.",
            pid, packet.ItemIndex, packet.Quantity,
            answer == 0 ? "success" : answer == 1 ? "rejected" : $"partial {insertedCount}");
        return true;
    }
}
