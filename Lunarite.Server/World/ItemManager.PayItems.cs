using System;
using System.Linq;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed partial class ItemManager
{
    // Balance as an int.
    internal void SendCashInfo(Client client)
    {
        int cash = _accountStore.GetCash((client.Session.AccountId ?? 0));
        client.SendPacket(new PayItem
        {
            Subtype = PayItemRequest.CashInfo, Result = PayItemResultCode.CashinfoSuccess,
            Extra = BitConverter.GetBytes(cash)
        });
    }

    // Purchases are free here and go to paid-item storage.
    internal void BuyPayItem(Client client, int packageSeq)
    {
        var catalogEntry = CashShopCatalog.ShopOpen ? _cashShopCatalog.FindPackage(packageSeq) : null;
        if (catalogEntry == null)
            client.SendPacket(new PayItem
            {
                Subtype = PayItemRequest.Buy, Result = PayItemResultCode.BuyFunctionFail
            });
        else
        {
            _payItemStore.Add((client.Session.AccountId ?? 0), catalogEntry);
            client.SendPacket(new PayItem
            {
                Subtype = PayItemRequest.Buy, Result = PayItemResultCode.BuySuccess,
                Extra = BitConverter.GetBytes(0)
            });
        }
    }

    // Paged storage rows.
    internal void SendPayItemStorage(Client client, byte requestedPage)
    {
        const int pageSize = 20;
        var stored = _payItemStore.Get((client.Session.AccountId ?? 0));
        int totalPages = Math.Max(1, (stored.Count + pageSize - 1) / pageSize);
        int page = Math.Clamp(requestedPage == 0 ? 1 : requestedPage, 1, totalPages);
        var pageItems = stored.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        using (var extra = new PacketWriter())
        {
            extra.Write((byte)pageItems.Length);
            extra.Write((byte)page);
            extra.Write((byte)totalPages);
            extra.Write(stored.Count);
            foreach (var item in pageItems)
            {
                extra.Write(item.CashName); extra.Write(item.SenderAccountId);
                extra.Write(item.StorageSeq); extra.Write(item.StorageItemSeq);
                extra.Write(0); extra.Write(0); extra.Write(item.ProductSeq);
                extra.Write(0f); extra.Write((byte)'C'); extra.Write((byte)0);
            }
            client.SendPacket(new PayItem
            {
                Subtype = PayItemRequest.QueryStorage, Result = PayItemResultCode.QueryStorageSuccess,
                Extra = extra.Buffer
            });
        }
    }

    // Claim a stored item into the inventory.
    internal void UsePayItem(Client client, int storageSeq, int storageItemSeq, byte itemType)
    {
        int accountId = (client.Session.AccountId ?? 0);
        int pid = Game.PidOf(client);
        if (pid <= 0 || itemType != (byte)'C'
            || !TryUsePayItem(accountId, pid, storageSeq, storageItemSeq,
                out var claimed, out var deposited))
        {
            client.SendPacket(new PayItem
            {
                Subtype = PayItemRequest.UseItem, Result = PayItemResultCode.UseItemFunctionFail
            });
            return;
        }
        using (var extra = new PacketWriter())
        {
            extra.Write(claimed.ItemId); extra.Write(claimed.ProductSeq);
            extra.Write(claimed.StorageSeq); extra.Write(claimed.StorageItemSeq); extra.Write((byte)0);
            client.SendPacket(new PayItem
            {
                Subtype = PayItemRequest.UseItem, Result = PayItemResultCode.UseItemSuccess,
                Extra = extra.Buffer
            });
        }
        SendInventoryItem(client, deposited);
    }

    // Gift to a character name, free here.
    internal void PresentPayItem(Client client, string? giftReceiver, int packageSeq)
    {
        var giftEntry = _cashShopCatalog.FindPackage(packageSeq);
        var receiver = _characterStore.FindByName(giftReceiver ?? "");
        int senderPid = Game.PidOf(client);
        var sender = _characterStore.Get(senderPid);
        if (!CashShopCatalog.ShopOpen || giftEntry == null || receiver == null || sender == null)
        {
            using var invalid = new PacketWriter();
            invalid.Write(0); invalid.Write(0);
            client.SendPacket(new PayItem
            {
                Subtype = PayItemRequest.PresentItem,
                Result = CashShopCatalog.ShopOpen
                    ? PayItemResultCode.PresentItemInvalidreceiverFail : PayItemResultCode.PresentItemFunctionFail,
                Extra = invalid.Buffer
            });
            return;
        }
        _payItemStore.Add(receiver.OwnerUserId, giftEntry, sender.Name);
        using (var sent = new PacketWriter())
        {
            sent.Write(0); sent.Write(0);
            client.SendPacket(new PayItem
            {
                Subtype = PayItemRequest.PresentItem, Result = PayItemResultCode.PresentItemSuccess,
                Extra = sent.Buffer
            });
        }
        if (Game.TryGetClient(receiver.Pid, out var receiverClient))
        {
            using var received = new PacketWriter();
            received.Write(sender.Name);
            receiverClient.SendPacket(new PayItem
            {
                Subtype = PayItemRequest.PresentItem, Result = PayItemResultCode.PresentItemReceived,
                Extra = received.Buffer
            });
        }
    }

    // Paid storage and inventory are separate stores; the claim commits both (and the possession
    // objective count) in one transaction, or startup replays them from the redo journal.
    public bool TryUsePayItem(int accountId, int pid, int storageSeq, int storageItemSeq,
        out PayItemStore.StoredItem claimed, out PlayerStore.GenericItem deposited)
    {
        PayItemStore.StoredItem taken = new();
        PlayerStore.GenericItem inserted = new();
        bool success = false;
        _multiStoreTransaction.Run(() =>
        {
            if (!_payItemStore.TryTake(accountId, storageSeq, storageItemSeq, out taken)) return;
            if (!_playerStore.TryAddMainItem(pid, _playerStore.CreateAcquiredItem(taken.ItemId),
                    out inserted))
            {
                _payItemStore.Restore(accountId, taken);
                return;
            }
            SynchronizeQuestPickupObjective(pid, taken.ItemId);
            success = true;
        });
        claimed = taken;
        deposited = inserted;
        return success;
    }

    // Exported DRINK_RESULT_CODE: OK=0, undefined/invalid=1, insufficient CR=2.
    // Cooked setters all have Price=0; the charge is kept for table variants with a price.
    public byte TryChargeDrink(int pid, int price)
    {
        if (pid <= 0 || price < 0) return 1;
        if (price == 0) return 0;
        return _playerStore.TrySpendMoney(pid, price) ? (byte)0 : (byte)2;
    }
}
