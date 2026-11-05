using System.Buffers.Binary;

using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void MailClaims()
    {
        var message = _transmitStore.Add(Recipient, Sender, 1, "Sender", "Subject", "Body", 750, attachments: _sent);
        var uniqueDelivery = _transmitStore.AddWithStatus(Recipient, 0, 3, "Quest NPC", "Unique", "Body", 700001);
        var duplicateDelivery = _transmitStore.AddWithStatus(Recipient, 0, 3, "Quest NPC", "Changed", "Replay", 700001);
        Check(uniqueDelivery.Created && !duplicateDelivery.Created
            && duplicateDelivery.Message.Sequence == uniqueDelivery.Message.Sequence
            && _transmitStore.List(Recipient).Count(x => x.Unique == 700001) == 1,
            "unique quest/system mail reports duplicate replay without creating a second delivery");
        Check(_transmitStore.UnreadTypes(Recipient).SequenceEqual(new byte[] { 1, 3 }),
            "offline mailbox state exposes each represented unread retail type for post-spawn notification");
        Check(TransmitManager.CanDeliverScriptedQuestMail(3, Sender, Sender, 0, 0, 0, 0)
            && TransmitManager.CanDeliverScriptedQuestMail(3, Sender, Recipient, 820300, Sender, 820300, Sender)
            && !TransmitManager.CanDeliverScriptedQuestMail(3, Sender, Recipient, 820300, Sender, 820300, Recipient)
            && !TransmitManager.CanDeliverScriptedQuestMail(3, Sender, Recipient, 820300, Sender, 820301, Sender)
            && !TransmitManager.CanDeliverScriptedQuestMail(2, Sender, Sender, 0, 0, 0, 0)
            && !TransmitManager.CanDeliverScriptedQuestMail(3, 0, Recipient, 0, 0, 0, 0),
            "quest Kismet mail may address self or an authenticated same-instance guest, not arbitrary players or GM mail");
        Check(message.Attachments.Count == 2, "attachments persisted on message");
        Check(!_transmitStore.Modify(Recipient, message.Sequence, 1), "mail with unclaimed value cannot be deleted");

        var first = _transmitStore.PeekAttachment(Recipient, message.Sequence, 0);
        Check(first?.Item.ItemId == 604221 && first.Item.Count == 3, "attachment metadata round-trips");
        Check(_playerStore.TryAddMainItem(Recipient, first!.Item, out var deposited), "attachment deposits into bag");
        Check(deposited.InventoryIndex == 0 && deposited.InventoryCell == 1 && deposited.Durability == 77, "deposit preserves state and finds first free slot");
        Check(_transmitStore.ClaimAttachment(Recipient, message.Sequence, 0), "attachment claim finalized");
        Check(_transmitStore.PeekAttachment(Recipient, message.Sequence, 0) == null, "attachment cannot be claimed twice");

        var second = _transmitStore.PeekAttachment(Recipient, message.Sequence, 1)!;
        Check(_playerStore.TryAddMainItem(Recipient, second.Item, out _), "second attachment deposits");
        Check(_transmitStore.ClaimAttachment(Recipient, message.Sequence, 1), "second attachment finalized");
        Check(_playerStore.GetItems(Recipient).OrderBy(x => x.item)
                .SequenceEqual(new[] { (100100, 1), (604221, 3) }),
            "claimed attachments enter aggregate recipient persistence");

        const int mailCapacityPid = 910003;
        ResetCharacter(mailCapacityPid);
        _playerStore.GetProgress(mailCapacityPid).InvenExpandStep = 2;
        _playerStore.GetInventory(mailCapacityPid, () => Enumerable.Range(0, 77)
            .Select(i => new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = (byte)i, ItemId = 710000 + i, Count = 1 })
            .ToList());
        Check(_playerStore.TryAddMainItem(mailCapacityPid,
                new PlayerStore.GenericItem { ItemId = 604221, Count = 1 }, out var finalMailCell)
            && finalMailCell.InventoryCell == 77
            && !_playerStore.TryAddMainItem(mailCapacityPid,
                new PlayerStore.GenericItem { ItemId = 604222, Count = 1 }, out _),
            "mail and auction deposits use all 78 retail MAIN cells and reject overflow");

        long before = _playerStore.GetProgress(Recipient).Money;
        Check(_transmitStore.TakeCR(Recipient, message.Sequence), "CR claim finalized");
        _playerStore.AddMoney(Recipient, 750);
        Check(_playerStore.GetProgress(Recipient).Money == before + 750, "recipient receives CR");
        Check(!_transmitStore.TakeCR(Recipient, message.Sequence), "CR cannot be claimed twice");
        Check(_transmitStore.Modify(Recipient, message.Sequence, 1), "empty mail can be deleted");

        var wire = new TransmitContent
        {
            MessageSequence = 12, Type = 1, From = "A", SenderPid = Sender, Title = "B", Content = "C", CR = 5,
            Attachments = [new(0, 604221, 3), new(1, 100100, 1)]
        }.Serialize();
        Check(wire[0] == 200, "content response opcode is 200");
        // Fixed tail is exactly 3 x (sequence,item,count), 36 bytes. Third slot must be empty.
        Check(BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(wire.Length - 12, 4)) == 0
            && BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(wire.Length - 8, 4)) == 0
            && BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(wire.Length - 4, 4)) == 0,
            "content response emits an empty third attachment triple");
        Check(new TransmitList().Serialize()[0] == 199
            && new TransmitSend { Answer = 1 }.Serialize()[0] == 201
            && new TransmitModify { Answer = 1 }.Serialize()[0] == 202
            && new TransmitGetItem { Answer = 1 }.Serialize()[0] == 203
            && new TransmitGetCR { Answer = 1 }.Serialize()[0] == 204
            && new TransmitArrive().Serialize()[0] == 205,
            "mail response family maps exactly to native server opcodes 199 through 205");
        Check(TransmitManager.TransmitPageSize == 8,
            "mailbox pages match the cooked 192px UIList_MsgList with 24px rows");
        var transmitListWire = new TransmitList
        {
            PageDirection = 1,
            CurrentPage = 0,
            TotalPages = 2,
            Entries = [new() { Sequence = 14, Type = 3, From = "", Title = "Retrieve the APC", Read = 0 }]
        }.Serialize();
        Check(transmitListWire.AsSpan(0, 5).SequenceEqual(new byte[] { 199, 1, 0, 2, 1 }),
            "mailbox list responses use PAGE_CUR so the native op199 parser consumes entries");
    }

    private void StarterInventoryAndDurations()
    {
        const int mailExpiryPid = 920074;
        _transmitStore.DeleteCharacter(mailExpiryPid);
        var expiringMail = _transmitStore.Add(mailExpiryPid, 0, 1, "System", "Timed items", "",
            attachments:
            [
                new PlayerStore.GenericItem { ItemId = 111201, Count = 1, LimitTime = 99, TuneUpOptions = 7 },
                new PlayerStore.GenericItem { ItemId = 111201, Count = 1, LimitTime = 101 },
                new PlayerStore.GenericItem { ItemId = 200199, Count = 1, LimitTime = 99 }
            ]);
        var expiredMail = _transmitStore.PruneExpiredAttachments(mailExpiryPid, 100);
        var remainingMail = _transmitStore.Get(mailExpiryPid, expiringMail.Sequence)!;
        Check(expiredMail is [{ MessageSequence: var messageSequence, AttachmentSequence: 0,
                ItemId: 111201, TuneUpOptions: 7, Type: 0 }]
            && messageSequence == expiringMail.Sequence
            && remainingMail.Attachments.Select(x => x.Sequence).SequenceEqual([1, 2])
            && _transmitStore.PruneExpiredAttachments(mailExpiryPid, 100).Count == 0,
            "elapsed mail attachments expire once while future timers and armor spray union fields remain");
        _transmitStore.DeleteCharacter(mailExpiryPid);
        const int emptyInventoryPid = 920073;
        ResetCharacter(emptyInventoryPid);
        _playerStore.GetInventory(emptyInventoryPid, () =>
        [
            new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 0, ItemId = 604221, Count = 1 }
        ]);
        Check(_playerStore.TryDropItem(emptyInventoryPid, 0, 0, 1, out _)
            && _playerStore.GetInventory(emptyInventoryPid, () =>
                [new PlayerStore.GenericItem { InventoryIndex = 3, InventoryCell = 0, ItemId = 100499 }]).Count == 0,
            "an initialized empty inventory remains empty instead of duplicating the starter loadout");
        const int possessedTimerPid = 920072;
        ResetCharacter(possessedTimerPid);
        _playerStore.GetInventory(possessedTimerPid, () => []);
        int possessedTimerNow = checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Check(_playerStore.TryAddQuestItems(possessedTimerPid, [(111201, 1)], out var possessedItems)
            && possessedItems is [{ LimitTime: > 0 }]
            && possessedItems[0].LimitTime >= possessedTimerNow + 1440 * 60
            && possessedItems[0].LimitTime <= possessedTimerNow + 1440 * 60 + 2,
            "EILT_Possessed duration starts from authoritative acquisition time");
    }
}
