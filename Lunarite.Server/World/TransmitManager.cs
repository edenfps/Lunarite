using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class TransmitManager
{
    private readonly BlockStore _blockStore;
    private readonly CharacterStore _characterStore;

    // UIMMOCommunity.MsgListScene is 192 px high with a 24 px RowHeight: eight rows.
    public const int TransmitPageSize = 8;

    // op199 carries page numbers as bytes; server-issued mail is uncapped and can exceed 255 pages.
    public static int TransmitPageCount(int messages) =>
        Math.Min(byte.MaxValue, (messages + TransmitPageSize - 1) / TransmitPageSize);
    private readonly object _transmitTransactionLock = new();
    // Not retail: one player mail per sender per interval. Each send rewrites the mail store, so an
    // unthrottled scripted client could stall every other store operation.
    public const long TransmitSendIntervalMs = 1000;
    internal readonly Dictionary<int, long> _lastTransmitSend = [];

    private readonly ILogger<TransmitManager> _logger;
    private readonly MultiStoreTransaction _multiStoreTransaction;
    private readonly TransmitStore _transmitStore;
    private readonly PlayerStore _playerStore;
    private readonly CharacterManager _characterManager;

    private readonly ItemManager _itemManager;

    public TransmitManager(BlockStore blockStore, CharacterStore characterStore,
        ILogger<TransmitManager> logger, MultiStoreTransaction multiStoreTransaction,
        TransmitStore transmitStore, PlayerStore playerStore,
        CharacterManager characterManager, ItemManager itemManager)
    {
        _itemManager = itemManager;
        _logger = logger;
        _multiStoreTransaction = multiStoreTransaction;
        _transmitStore = transmitStore;
        _playerStore = playerStore;
        _characterManager = characterManager;
        _blockStore = blockStore;
        _characterStore = characterStore;
    }

    // HuxleySeqAct_SendMessage is the active SendTransmitSendSystem caller. It uses
    // TRANSMITTYPE_QUEST (3) and addresses the target PlayerController's name. A listen host
    // can only target a guest in the same quest instance.
    public static bool CanDeliverScriptedQuestMail(byte type, int senderPid, int recipientPid,
        int senderQuest, int senderInstance, int recipientQuest, int recipientInstance) =>
        type == 3 && senderPid > 0 && recipientPid > 0
        && (senderPid == recipientPid || (senderQuest > 0 && senderInstance > 0
            && recipientQuest == senderQuest && recipientInstance == senderInstance));

    // Not retail: scripted quest mail (op181) one sender may deliver per window. A quest step can
    // address every party member at once (up to four) and chain a second message, so a burst of
    // ten fits; a modified client looping op181 is held to one rewrite of transmits.json per second.
    public const int ScriptedMailBurst = 10;
    public const long ScriptedMailWindowMs = 10_000;
    private readonly object _scriptedMailLock = new();
    internal readonly Dictionary<int, Queue<long>> _scriptedMailSends = [];

    public static bool IsScriptedMailThrottled(Dictionary<int, Queue<long>> sends, int senderPid, long nowMs,
        int burst = ScriptedMailBurst, long windowMs = ScriptedMailWindowMs)
    {
        if (!sends.TryGetValue(senderPid, out var times)) return false;
        while (times.Count > 0 && nowMs - times.Peek() >= windowMs) times.Dequeue();
        return times.Count >= burst;
    }

    public static void RecordScriptedMail(Dictionary<int, Queue<long>> sends, int senderPid, long nowMs)
    {
        if (!sends.TryGetValue(senderPid, out var times)) sends[senderPid] = times = new Queue<long>();
        times.Enqueue(nowMs);
    }

    // op181 delivery after the quest-authority check. Answers TRANSMITRET_DUPLICATE (10) for a
    // repeated unique ID and TRANSMITRET_ERROR when throttled or at the recipient's daily limit.
    internal (byte Answer, bool Created) TransmitSendSystem(int senderPid, int recipientPid,
        TransmitSendSystemRequest request, long? nowMs = null, long? utcDay = null)
    {
        lock (_scriptedMailLock)
        {
            long now = nowMs ?? Environment.TickCount64;
            if (IsScriptedMailThrottled(_scriptedMailSends, senderPid, now))
                return (TransmitResultCode.Error, false);
            var status = _transmitStore.AddScripted(recipientPid, request.TransmitType, request.FromName ?? "",
                request.Title ?? "", request.Content ?? "", request.Id, utcDay ?? UtcDay());
            if (status == TransmitStore.ScriptedMailStatus.Duplicate) return (10, false);
            if (status == TransmitStore.ScriptedMailStatus.DailyLimit) return (TransmitResultCode.Error, false);
            RecordScriptedMail(_scriptedMailSends, senderPid, now);
            return (TransmitResultCode.Ok, true);
        }
    }

    // A recipient's block list refuses mail with TRANSMITRET_BLOCKEDPLAYER. The op205 arrival
    // notice goes to the recipient's connection, not back to the sender.
    public bool CanSendTransmit(int senderPid, int recipientPid) =>
        senderPid > 0 && recipientPid > 0 && !_blockStore.List(recipientPid).Contains(senderPid);

    // HuxleyMsgListMenu's send-button callback passes its local PlayerIndex as the first native
    // SendTransmitSend argument, not the addressee's PID, so the typed name wins even when that
    // index matches some other character's PID.
    public CharacterStore.Character? ResolveTransmitRecipient(int playerIndex, string? toName)
    {
        _ = playerIndex;
        if (string.IsNullOrWhiteSpace(toName)) return null;
        return _characterStore.All().FirstOrDefault(x =>
            string.Equals(x.Name, toName, StringComparison.OrdinalIgnoreCase));
    }

    // Not retail: player mail one sender may deliver per UTC day. Each delivered message stays in
    // transmits.json until its recipient deletes it, so this bounds what one character can store.
    public const int TransmitDailySendLimit = 200;
    internal readonly Dictionary<int, (long Day, int Count)> _dailyTransmitSends = [];

    // Reports whether the sender is still inside the send interval. Only a delivered message
    // starts the interval (RecordTransmitSend), so a refused send can be corrected and retried.
    public static bool IsTransmitSendThrottled(Dictionary<int, long> lastSend, int senderPid, long nowMs,
        long intervalMs = TransmitSendIntervalMs) =>
        lastSend.TryGetValue(senderPid, out long last) && nowMs - last < intervalMs;

    public static bool IsTransmitDailyLimitReached(Dictionary<int, (long Day, int Count)> dailySends,
        int senderPid, long utcDay, int limit = TransmitDailySendLimit) =>
        dailySends.TryGetValue(senderPid, out var sent) && sent.Day == utcDay && sent.Count >= limit;

    public static void RecordTransmitSend(Dictionary<int, long> lastSend,
        Dictionary<int, (long Day, int Count)> dailySends, int senderPid, long nowMs, long utcDay)
    {
        lastSend[senderPid] = nowMs;
        dailySends[senderPid] = dailySends.TryGetValue(senderPid, out var sent) && sent.Day == utcDay
            ? (utcDay, sent.Count + 1) : (utcDay, 1);
    }

    internal static long UtcDay() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86_400;

    internal static void NotifyTransmitArrival(int recipientPid, byte transmitType)
    {
        if (Game.TryGetClient(recipientPid, out var recipientClient))
            recipientClient.SendPacket(new TransmitArrive { TransmitType = transmitType });
    }

    // A claim that would push the balance past PlayerStore.MaxMoney leaves the CR on the mail.
    // enum_TRANSMIT_RESULT has no max-money entry, so it answers TRANSMITRET_ERROR.
    internal (byte Answer, int Amount) TransmitGetCR(int pid, int messageSequence)
    {
        byte answer = TransmitResultCode.NotfoundPost;
        int amount = 0;
        lock (_transmitTransactionLock)
        {
            _multiStoreTransaction.Run(() =>
            {
                amount = _transmitStore.Get(pid, messageSequence)?.CR ?? 0;
                if (amount <= 0) return;
                // The cap check and the credit are one atomic step, so a concurrent trade, vendor or
                // reward credit cannot truncate the claim.
                if (!_playerStore.TryAddMoney(pid, amount))
                {
                    answer = TransmitResultCode.Error;
                    return;
                }
                if (!_transmitStore.TakeCR(pid, messageSequence))
                {
                    _playerStore.TrySpendMoney(pid, amount);
                    return;
                }
                answer = TransmitResultCode.Ok;
            });
        }
        return (answer, amount);
    }

    internal void TransmitGetItem(Client client, int pid, TransmitGetItemRequest request)
    {
        lock (_transmitTransactionLock)
        {
            byte answer = 0;
            PlayerStore.GenericItem? claimed = null;
            _multiStoreTransaction.Run(() =>
            {
                var attachment = _transmitStore.PeekAttachment(pid, request.MessageSequence, request.ItemSequence);
                if (attachment == null)
                {
                    answer = TransmitResultCode.NotfoundPost;
                    return;
                }
                if (!_playerStore.TryAddMainItem(pid, attachment.Item, out var deposited))
                {
                    answer = TransmitResultCode.InvenFull;
                    return;
                }
                if (!_transmitStore.ClaimAttachment(pid, request.MessageSequence, request.ItemSequence))
                {
                    _playerStore.RemoveMainItem(pid, deposited.InventoryCell);
                    answer = TransmitResultCode.NotfoundPost;
                    return;
                }
                _itemManager.SynchronizeQuestPickupObjective(pid, deposited.ItemId);
                claimed = deposited;
            });
            client.SendPacket(new TransmitGetItem
            {
                Answer = answer,
                MessageSequence = request.MessageSequence,
                ItemSequence = request.ItemSequence,
                Item = claimed == null ? new byte[32] : ItemManager.SerializeGenericItem(claimed)
            });
            if (claimed != null)
                _logger.LogInformation("[Transmit] PID {0} claimed item {1}x{2} from message {3} attachment {4}.",
                    pid, claimed.ItemId, claimed.Count, request.MessageSequence, request.ItemSequence);
        }
    }

    // Takes the attachments and CR from the sender and delivers the message in one transaction.
    internal void TransmitSend(Client client, TransmitSendRequest request,
        CharacterStore.Character sender, CharacterStore.Character recipient)
    {
        byte[] cells = [request.ItemCell1, request.ItemCell2, request.ItemCell3];
        lock (_transmitTransactionLock)
        {
            bool sent = false;
            byte answer = 0;
            long nowMs = Environment.TickCount64, utcDay = UtcDay();
            if (IsTransmitSendThrottled(_lastTransmitSend, sender.Pid, nowMs)
                || IsTransmitDailyLimitReached(_dailyTransmitSends, sender.Pid, utcDay)
                || _transmitStore.IsMailboxFull(recipient.Pid))
                answer = TransmitResultCode.Error;
            else _multiStoreTransaction.Run(() =>
            {
                // Text-only mail leaves inventory and money untouched, so their stores are not rewritten.
                List<PlayerStore.GenericItem> attachments = [];
                if (cells.Any(cell => cell != byte.MaxValue)
                    && !_playerStore.TryRemoveMainItems(sender.Pid, cells, out attachments,
                        out bool boundItem, out bool cannotEnclose))
                {
                    answer = boundItem ? (byte)4 : cannotEnclose ? (byte)13 : (byte)5;
                    return;
                }
                if (request.CR != 0 && !_playerStore.TrySpendMoney(sender.Pid, request.CR))
                {
                    _playerStore.RestoreMainItems(sender.Pid, attachments);
                    answer = TransmitResultCode.NotEnoughMoney;
                    return;
                }
                _transmitStore.Add(recipient.Pid, sender.Pid, 1, sender.Name,
                    request.Title ?? "", request.Content ?? "", request.CR, attachments: attachments);
                _itemManager.SynchronizeQuestPickupObjectives(sender.Pid, attachments.Select(item => item.ItemId));
                sent = true;
            });
            if (sent) RecordTransmitSend(_lastTransmitSend, _dailyTransmitSends, sender.Pid, nowMs, utcDay);
            client.SendPacket(new TransmitSend
            {
                Answer = answer,
                ItemCell1 = sent ? request.ItemCell1 : (byte)0,
                ItemCell2 = sent ? request.ItemCell2 : (byte)0,
                ItemCell3 = sent ? request.ItemCell3 : (byte)0
            });
            if (sent)
            {
                _characterManager.SendMoneyParameter(client, sender.Pid);
                NotifyTransmitArrival(recipient.Pid, 1); // TRANSMITTYPE_NORMAL
                _logger.LogInformation("[Transmit] PID {0} -> {1}: CR={2}, attachments sent.",
                    sender.Pid, recipient.Pid, request.CR);
            }
        }
    }
}
