using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AuctionBidHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static CharacterManager _characterManager = null!;
    private static ItemManager _itemManager = null!;
    private static AuctionStore _auctionStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _auctionStore = serviceProvider.GetRequiredService<AuctionStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int index) || !reader.TryRead(out byte category)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        AuctionStore.Result result = AuctionStore.Result.Error;
        PlayerStore.GenericItem? purchased = null;
        AuctionStore.AuctionItemInfo? sold = null;
        _multiStoreTransaction.Run(() =>
        {
            result = _auctionStore.Bid(pid, index, out purchased, out sold, category);
            if (result == AuctionStore.Result.Success && purchased != null)
                _itemManager.SynchronizeQuestPickupObjective(pid, purchased.ItemId);
        });
        client.SendPacket(new AuctionResult { Opcode = 166, Answer = (byte)result,
            ItemIndex = result == AuctionStore.Result.Success ? purchased?.ItemId : null });
        if (purchased != null)
        {
            ItemManager.SendInventoryItem(client, purchased);
            _characterManager.SendMoneyParameter(client, pid);
        }
        if (result == AuctionStore.Result.Success && sold != null
            && Game.TryGetClient(sold.SellerPid, out var sellerClient))
        {
            sellerClient.SendPacket(new AuctionNotification
            {
                Opcode = 169, Result = (byte)AuctionStore.Result.Success, ItemId = sold.Item.ItemId
            });
            // OnAuctionSold carries only result + item ID, so the new balance is sent separately.
            _characterManager.SendMoneyParameter(sellerClient, sold.SellerPid);
        }
        return true;
    }
}
