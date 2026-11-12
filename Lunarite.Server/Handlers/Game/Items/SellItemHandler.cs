using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SellItemHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemCatalog _itemCatalog = null!;
    private static ItemManager _itemManager = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestStore _questStore = null!;
    private static QuestCatalog _questCatalog = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _itemCatalog = serviceProvider.GetRequiredService<ItemCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
    }

    // A collected quest item (ItemCatalog.IsSellableQuestPickup) stays unsellable while a quest in the journal still
    // collects it, so nobody sells away an active quest's progress.
    public static bool IsNeededByActiveQuest(System.Collections.Generic.IEnumerable<QuestCatalog.Quest?> acquired, int itemId) =>
        acquired.Any(quest => quest?.Objectives.Any(objective =>
            objective.BattleZoneObjective == "FQOType_PickItem" && objective.ExtraParam == itemId) == true);

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte cell) || !reader.TryRead(out int count)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var existing = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell == cell);
        var data = existing == null ? null : _itemCatalog.Get(existing.ItemId);
        PlayerStore.GenericItem? remaining = null;
        long money = _playerStore.GetProgress(pid).Money;
        long saleProceeds = 0;
        bool sold = false;
        byte sellResult = PlayerStore.SellNotExist;
        if (data != null && ItemCatalog.IsSellableQuestPickup(data.EntryIndex, data.SellPrice)
            && IsNeededByActiveQuest(_questStore.Acquired(pid).Select(_questCatalog.Get), data.EntryIndex))
        {
            data = null;
            sellResult = PlayerStore.SellCannotSell;
        }
        if (data != null)
            _multiStoreTransaction.Run(() =>
            {
                sold = _playerStore.TrySellMainItem(pid, cell, count, data,
                    out remaining, out money, out saleProceeds, out sellResult);
                if (sold) _itemManager.SynchronizeQuestPickupObjective(pid, existing!.ItemId);
            });
        if (sold)
        {
            if (remaining != null)
                client.SendPacket(new UpdateItemNum { Inventory = InventoryType.Main, Cell = cell, Count = remaining.Count });
            else
                client.SendPacket(new RemoveItem { InventoryIndex = InventoryType.Main, Cell = cell }); // see DropItemHandler
            client.SendPacket(new Parameter
            {
                Param = Parameter.PARAMETER_MONEY, Value = (int)Math.Min(int.MaxValue, money)
            });
            // Undo charges back the credited proceeds, after the sell-rate multiplier and truncation.
            int displayedPrice = checked((int)Math.Min(int.MaxValue, saleProceeds));
            var source = existing!; // sold implies the cell held an item
            var soldItem = new PlayerStore.GenericItem
            {
                ItemId = source.ItemId, Count = count, InventoryIndex = InventoryType.Main, InventoryCell = source.InventoryCell,
                State = source.State, LimitTime = source.LimitTime, TuneUpOptions = source.TuneUpOptions,
                Skill = source.Skill, Skill1 = source.Skill1, Durability = source.Durability
            };
            var undo = Game.PlayerFor(pid).UndoSalesOrCreate();
            byte serial;
            lock (undo)
            {
                do serial = _itemManager.NextUndoSerial(); while (undo.Any(x => x.Serial == serial));
                undo.Add(new UndoItem(serial, soldItem, saleProceeds));
            }
            client.SendPacket(new UndoItemInsert
                { Serial = serial, ItemId = soldItem.ItemId, Count = checked((ushort)count), Price = displayedPrice });
        }
        client.SendPacket(new SellItem { Result = sellResult });
        return true;
    }
}
