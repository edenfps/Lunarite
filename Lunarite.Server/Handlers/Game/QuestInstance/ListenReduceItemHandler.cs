using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ListenReduceItemHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ItemManager _itemManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ListenReduceItemHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native SendListen_ReduceItem op116: `dbbd` = PID, inventory, cell, quantity.
        if (!reader.TryRead(out int targetPid) || !reader.TryRead(out byte inventory)
            || !reader.TryRead(out byte cell) || !reader.TryRead(out int quantity)
            || reader.RemainingLength != 0) return false;
        int reporterPid = Game.PidOf(client);
        int questIndex = _questRoomMembers.QuestOf(client.Id);
        // Sent only from an NM_ListenServer by the special-skill code. The PRI PID is either the
        // host's room PID (quest index) or a guest's character PID, resolved like other listen
        // reports to a participant of the same instance.
        int ownerPid = questIndex > 0 ? _questInstanceManager.ResolveQuestReportOwner(client, targetPid, questIndex) : 0;
        var slotItem = ownerPid > 0 ? _playerStore.GetInventory(ownerPid, () => [])
            .FirstOrDefault(item => item.InventoryIndex == inventory && item.InventoryCell == cell) : null;
        int itemId = slotItem?.ItemId ?? 0;
        PlayerStore.GenericItem? remaining = null;
        bool reduced = false;
        // The only sender, HuxleyLicenseSkill_Special.ConsumeBattleItem, always reduces one item of
        // the belt's first cell: SendListen_ReduceItem(PID, INVEN_BELT, 0, 1).
        // A host reducing a guest's belt is held to the stack the guest brought into the run.
        if (ownerPid > 0 && inventory == InventoryType.Belt && cell == 0 && quantity == 1
            && (ownerPid == reporterPid
                || _questInstanceManager.TryAcceptBeltReduction(_questRoomMembers.InstanceOf(client.Id), ownerPid, slotItem?.Count ?? 0)))
            _multiStoreTransaction.Run(() =>
            {
                reduced = _playerStore.TryDropItem(ownerPid, inventory, cell, quantity, out remaining);
                if (reduced && inventory == InventoryType.Main && itemId > 0)
                    _itemManager.SynchronizeQuestPickupObjective(ownerPid, itemId);
            });
        if (!reduced)
        {
            _logger.LogWarning(
                "[Listen] rejected item reduction reporter {0}, wire target {1}, owner {2}, slot {3}:{4}, count {5}.",
                reporterPid, targetPid, ownerPid, inventory, cell, quantity);
            return true;
        }
        if (Game.TryGetClient(ownerPid, out var targetClient))
        {
            targetClient.SendPacket(new RemoveItem { InventoryIndex = inventory, Cell = cell });
            if (remaining != null) ItemManager.SendInventoryItem(targetClient, remaining);
        }
        return true;
    }
}
