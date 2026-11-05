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
public static class InsertBankHandler
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
        if (!reader.TryRead(out byte mainCell) || !reader.TryRead(out byte bankCell)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var before = _playerStore.GetInventory(pid, () => []);
        int[] affected = before.Where(item => (item.InventoryIndex == InventoryType.Main && item.InventoryCell == mainCell)
                || (item.InventoryIndex == InventoryType.Bank && item.InventoryCell == bankCell))
            .Select(item => item.ItemId).Where(itemId => itemId > 0).Distinct().ToArray();
        bool moved = false;
        if (bankCell < _playerStore.BankCapacity(pid) && mainCell < _playerStore.MainCapacity(pid))
            _multiStoreTransaction.Run(() =>
            {
                moved = _playerStore.SwapSlots(pid, InventoryType.Bank, bankCell, InventoryType.Main, mainCell);
                if (moved) _itemManager.SynchronizeQuestPickupObjectives(pid, affected);
            });
        client.SendPacket(new InsertBank
        {
            Result = moved ? (byte)0 : (byte)1, MainCell = mainCell, BankCell = bankCell
        });
        return true;
    }
}
