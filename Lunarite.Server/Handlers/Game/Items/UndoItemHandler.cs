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
public static class UndoItemHandler
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
        if (!reader.TryRead(out byte serial) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        if (pid <= 0 || Game.PlayerFor(pid).UndoSales is not { } undo)
        {
            client.SendPacket(new UndoItemResult { Result = ItemResultCode.Notexist });
            return true;
        }

        UndoItem? sale;
        lock (undo) sale = undo.FirstOrDefault(x => x.Serial == serial);
        if (sale == null)
        {
            client.SendPacket(new UndoItemResult { Result = ItemResultCode.Notexist });
            return true;
        }
        if (_playerStore.GetProgress(pid).Money < sale.Price)
        {
            client.SendPacket(new UndoItemResult { Result = ItemResultCode.NotEnoughMoney });
            return true;
        }
        PlayerStore.GenericItem? deposited = null;
        long money = _playerStore.GetProgress(pid).Money;
        bool restored = false;
        _multiStoreTransaction.Run(() =>
        {
            restored = _playerStore.TryUndoSale(pid, sale.Item, sale.Price, out deposited, out money);
            if (restored) _itemManager.SynchronizeQuestPickupObjective(pid, sale.Item.ItemId);
        });
        if (!restored || deposited == null)
        {
            client.SendPacket(new UndoItemResult { Result = ItemResultCode.InvenFull });
            return true;
        }

        lock (undo) undo.Remove(sale);
        client.SendPacket(new UndoItemResult { Result = ItemResultCode.Ok });
        client.SendPacket(new UndoItemRemove { Serial = serial });
        ItemManager.SendInventoryItem(client, deposited);
        client.SendPacket(new Parameter
            { Param = Parameter.PARAMETER_MONEY, Value = (int)Math.Min(int.MaxValue, money) });
        return true;
    }
}
