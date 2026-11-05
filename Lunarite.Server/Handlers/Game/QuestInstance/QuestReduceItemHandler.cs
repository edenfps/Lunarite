using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestReduceItemHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestReduceItemHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestItemRequest.TryDeserialize(ref reader, out var packet)) return false;
        int pid = Game.PidOf(client);
        int questIndex = _questRoomMembers.QuestOf(client.Id);
        int instanceToken = _questRoomMembers.InstanceOf(client.Id);
        bool removed = false;
        List<PlayerStore.GenericItem> before = [], after = [];
        bool cityScriptReduction = ItemManager.IsCityScriptItemReductionAuthorized(pid,
            (Game.PlayerFor(pid).CityMap ?? -1), instanceToken, packet.ItemIndex, packet.Quantity);
        if ((_questInstanceManager.IsQuestLootRequestAuthorized(pid, questIndex, instanceToken) || cityScriptReduction)
            && packet.ItemIndex > 0 && packet.Quantity >= 0)
            _multiStoreTransaction.Run(() =>
            {
                before = _playerStore.GetInventorySnapshot(pid, () => _itemManager.BuildInitialInventory(pid));
                if (!_playerStore.TryReduceItem(pid, packet.ItemIndex, packet.Quantity,
                        cityScriptReduction ? InventoryType.Main : null)) return;
                _itemManager.SynchronizeQuestPickupObjective(pid, packet.ItemIndex);
                after = _playerStore.GetInventorySnapshot(pid, () => _itemManager.BuildInitialInventory(pid));
                removed = true;
            });
        if (removed)
            foreach (byte inventory in before.Concat(after).Select(item => item.InventoryIndex).Distinct())
                foreach (var change in ItemManager.BuildInventorySlotChanges(before, after, inventory))
                    client.SendPacket(change);
        _logger.LogInformation("[Quest] op50 reduce item PID {0}: item={1}, quantity={2}, applied={3}.",
            pid, packet.ItemIndex, packet.Quantity, removed);
        return true;
    }
}
