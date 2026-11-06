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

// SendQuestInsertDropItemAtOnce -> op48 "dm". The rally-end menu moves collected pickups to
// inventory, capped by what op56/op57 issued this run. Reply op51 releases the transaction lock.
[PacketHandler]
public static class QuestInsertDropItemAtOnceHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ItemManager _itemManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestInsertDropItemAtOnceHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestInsertDropItemAtOnceRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("op48 SendQuestInsertDropItemAtOnce: malformed payload.");
            return false;
        }

        int pid = Game.PidOf(client);
        List<PlayerStore.GenericItem> inserted = [];
        List<(int ItemId, int Entitled, int Inserted)> claims = [];
        bool valid = false;
        // The mission-end screen can report after the player left the instance (see TryResolveLootClaim).
        // Each item is capped at what this run issued and inserted as far as MAIN has room.
        if (_questInstanceManager.TryResolveLootClaim(client.Id, pid, out int instanceToken, out int poolPid))
            valid = QuestLootGrants.TryClaimUpTo(instanceToken, poolPid,
                packet.Items.Select(x => (x.ItemIndex, x.Num)), clamp: true, entitled =>
            {
                int[]? placed = null;
                _multiStoreTransaction.Run(() =>
                {
                    if (!_playerStore.TryAddQuestItemsUpTo(pid, entitled, out inserted, out var counts)) return;
                    for (int i = 0; i < entitled.Count; i++)
                        if (counts[i] > 0) _itemManager.SynchronizeQuestPickupObjective(pid, entitled[i].ItemId);
                    placed = counts;
                });
                return placed;
            }, out claims);

        if (valid)
        {
            foreach (var item in inserted) ItemManager.SendGrantedItem(client, item);
        }

        // OnInsertDropItem_AtOnce_Quest empties the reward panel before reading the result and
        // refills it only for INSERT_PROTION, from this list. So a partial insert answers 29 with
        // what is still uncollected; anything else would drop the leftovers from the menu.
        var leftovers = claims.Where(x => x.Inserted < x.Entitled)
            .Select(x => (x.ItemId, x.Entitled - x.Inserted)).ToList();
        byte answer = !valid ? (byte)1 : leftovers.Count == 0 ? (byte)0 : ItemResultCode.InsertProtion;
        client.SendPacket(new InsertDropItemAtOnceQuest
        {
            Answer = answer,
            Items = answer == ItemResultCode.InsertProtion ? leftovers : packet.Items
        });
        _logger.LogInformation("*** op48 SendQuestInsertDropItemAtOnce (PID {0}, result {1}): {2}", pid,
            answer == 0 ? "success" : answer == 1 ? "rejected" : "partial",
            string.Join(", ", packet.Items.Select(x => $"{x.ItemIndex}x{x.Num}")));
        return true;
    }
}
