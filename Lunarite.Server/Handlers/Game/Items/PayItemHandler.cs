using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class PayItemHandler
{
    private static ItemManager _itemManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte subtype)) return false;
        int packageSeq = 0, storageSeq = 0, storageItemSeq = 0;
        byte requestedPage = 1, itemType = 0;
        string? giftReceiver = null;
        bool valid = subtype switch
        {
            PayItemRequest.CashInfo or PayItemRequest.Version or PayItemRequest.CanIOpenIngameshop => reader.RemainingLength == 0,
            PayItemRequest.QueryStorage => reader.TryRead(out requestedPage) && reader.RemainingLength == 0,
            PayItemRequest.Buy => reader.TryRead(out packageSeq) && reader.TryRead(out int _) && reader.TryRead(out int _)
                 && reader.TryRead(out byte _) && reader.RemainingLength == 0,
            PayItemRequest.UseItem => reader.TryRead(out storageSeq) && reader.TryRead(out storageItemSeq)
                 && reader.TryRead(out itemType)
                 && reader.RemainingLength == 0,
            PayItemRequest.PresentItem => reader.TryRead(out giftReceiver) && reader.TryRead(out string? _)
                 && reader.TryRead(out packageSeq) && reader.TryRead(out int _) && reader.TryRead(out int _)
                 && reader.TryRead(out byte _) && reader.RemainingLength == 0,
            _ => false
        };
        if (!valid) return false;
        switch (subtype)
        {
            case PayItemRequest.CashInfo:
                _itemManager.SendCashInfo(client);
                break;
            case PayItemRequest.Buy:
                _itemManager.BuyPayItem(client, packageSeq);
                break;
            case PayItemRequest.QueryStorage:
                _itemManager.SendPayItemStorage(client, requestedPage);
                break;
            case PayItemRequest.UseItem:
                _itemManager.UsePayItem(client, storageSeq, storageItemSeq, itemType);
                break;
            case PayItemRequest.PresentItem:
                _itemManager.PresentPayItem(client, giftReceiver, packageSeq);
                break;
            case PayItemRequest.Version: // three int versions
                client.SendPacket(new PayItem
                {
                    Subtype = PayItemRequest.Version, Result = PayItemResultCode.VersionSuccess, Extra = new byte[12]
                });
                break;
            case PayItemRequest.CanIOpenIngameshop:
                client.SendPacket(new PayItem
                {
                    Subtype = PayItemRequest.CanIOpenIngameshop, Result = CashShopCatalog.ShopOpen
                        ? PayItemResultCode.CaniopeningameshopSuccess : PayItemResultCode.CaniopeningameshopFail
                });
                break;
            default:
                client.SendPacket(new PayItem { Subtype = subtype, Result = PayItemResultCode.SystemConnection });
                break;
        }
        return true;
    }
}
