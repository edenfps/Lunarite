using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;

namespace Lunarite.Storage;

// Persistent retail-style mailbox, keyed by recipient character PID.
public sealed class TransmitStore
{
    private readonly ILogger _logger;

    public sealed record ExpiredAttachment(int MessageSequence, int AttachmentSequence,
        int ItemId, int TuneUpOptions, byte Type);

    public sealed class TransmitInfo
    {
        public int Sequence { get; set; }

        public int RecipientPid { get; set; }

        public int SenderPid { get; set; }

        public byte Type { get; set; }

        public string From { get; set; } = "";

        public string Title { get; set; } = "";

        public string Content { get; set; } = "";

        public int CR { get; set; }

        public bool Read { get; set; }

        public int Unique { get; set; }

        // UTC day (Unix seconds / 86400) the message was stored; 0 on messages saved before it existed.
        public long Day { get; set; }

        public List<TransmitContentItemInfo> Attachments { get; set; } = [];
    }

    public sealed class TransmitContentItemInfo
    {
        // Stable per-message identifier echoed by op201 and supplied back in op183.
        public int Sequence { get; set; }

        public PlayerStore.GenericItem Item { get; set; } = new();

        public bool Claimed { get; set; }
    }

    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "transmits.json");
    private readonly object _lock = new();
    private readonly List<TransmitInfo> _messages;

    private readonly ItemCatalog _itemCatalog;

    private readonly AtomicJson _atomicJson;

    public TransmitStore(ILogger<TransmitStore> logger, ItemCatalog itemCatalog, AtomicJson atomicJson)
    {
        _logger = logger;
        _itemCatalog = itemCatalog;
        _atomicJson = atomicJson;
        _messages = Load();
        _lastSequence = _messages.Count == 0 ? 0 : _messages.Max(x => x.Sequence);
    }

    // Highest sequence issued; a running counter so each add does not rescan every stored message.
    private int _lastSequence;

    private List<TransmitInfo> Load()
    {
        try
        {
            return _atomicJson.Read<List<TransmitInfo>>(_path) ?? [];
        }
        catch (Exception ex) { _logger.LogWarning("[Transmit] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try { _atomicJson.Write(_path, JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { _logger.LogWarning("[Transmit] save failed: {0}", ex.Message); }
    }

    // Not retail (no TRANSMITRET code for a full box): player mail (op180) stops at this many
    // player messages per recipient, so a scripted sender cannot grow transmits.json or the
    // recipient's op199 page count without bound. Scripted quest mail and server-issued mail
    // neither count nor are refused.
    public const int MailboxCapacity = 200;

    public bool IsMailboxFull(int recipientPid)
    {
        lock (_lock)
            return _messages.Count(x => x.RecipientPid == recipientPid && x.Type == PlayerMailType) >= MailboxCapacity;
    }

    private const byte PlayerMailType = 1; // TRANSMITTYPE_NORMAL

    public TransmitInfo Add(int recipientPid, int senderPid, byte type, string from, string title,
        string content, int cr = 0, int unique = 0, IEnumerable<PlayerStore.GenericItem>? attachments = null)
        => AddCore(recipientPid, senderPid, type, from, title, content, cr, unique, attachments).Message;

    // Quest/system messages carry a caller-supplied unique ID; a repeat delivery is reported as
    // TRANSMITRET_DUPLICATE and raises no new arrival alert.
    public (TransmitInfo Message, bool Created) AddWithStatus(int recipientPid, int senderPid,
        byte type, string from, string title, string content, int unique)
        => AddCore(recipientPid, senderPid, type, from, title, content, 0, unique, null);

    public enum ScriptedMailStatus { Created, Duplicate, DailyLimit }

    // Not retail: scripted quest mail (op181) one recipient may receive per UTC day, counted from
    // the stored messages so a restart does not reset it. Quest Kismet sends one or two messages
    // per quest step, so this is far above a full day of normal questing while still bounding
    // transmits.json.
    public const int ScriptedMailDailyLimit = 200;

    // The duplicate check runs first, so a repeated delivery answers TRANSMITRET_DUPLICATE even
    // when the recipient is at the daily limit.
    public ScriptedMailStatus AddScripted(int recipientPid, byte type, string from, string title,
        string content, int unique, long utcDay, int dailyLimit = ScriptedMailDailyLimit)
    {
        lock (_lock)
        {
            if (unique != 0 && _messages.Any(x => x.RecipientPid == recipientPid && x.Unique == unique))
                return ScriptedMailStatus.Duplicate;
            if (_messages.Count(x => x.RecipientPid == recipientPid && x.Type == type && x.SenderPid == 0
                    && x.Day == utcDay) >= dailyLimit)
                return ScriptedMailStatus.DailyLimit;
            AddCore(recipientPid, 0, type, from, title, content, 0, unique, null, utcDay);
            return ScriptedMailStatus.Created;
        }
    }

    private (TransmitInfo Message, bool Created) AddCore(int recipientPid, int senderPid, byte type,
        string from, string title, string content, int cr, int unique,
        IEnumerable<PlayerStore.GenericItem>? attachments, long? utcDay = null)
    {
        lock (_lock)
        {
            if (unique != 0)
            {
                var duplicate = _messages.FirstOrDefault(x => x.RecipientPid == recipientPid && x.Unique == unique);
                if (duplicate != null) return (duplicate, false);
            }

            int sequence = ++_lastSequence;
            var message = new TransmitInfo
            {
                Sequence = sequence,
                RecipientPid = recipientPid,
                SenderPid = senderPid,
                Type = type,
                From = from,
                Title = title,
                Content = content,
                CR = cr,
                Unique = unique,
                Day = utcDay ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86_400,
                Attachments = attachments?.Select((item, index) => new TransmitContentItemInfo
                {
                    Sequence = index,
                    Item = CloneItem(item)
                }).ToList() ?? []
            };
            _messages.Add(message);
            Save();
            return (message, true);
        }
    }

    public IReadOnlyList<TransmitInfo> List(int recipientPid)
    {
        lock (_lock)
            return _messages.Where(x => x.RecipientPid == recipientPid).OrderByDescending(x => x.Sequence).ToList();
    }

    // The city HUD clears its new-mail marker on init and restores it only from op205, sent once
    // per unread type after city spawn.
    public IReadOnlyList<byte> UnreadTypes(int recipientPid)
    {
        lock (_lock)
            return _messages.Where(x => x.RecipientPid == recipientPid && !x.Read
                    && x.Type is >= 1 and <= 6)
                .Select(x => x.Type).Distinct().OrderBy(x => x).ToArray();
    }

    public TransmitInfo? Get(int recipientPid, int sequence)
    {
        lock (_lock) return _messages.FirstOrDefault(x => x.RecipientPid == recipientPid && x.Sequence == sequence);
    }

    public bool Modify(int recipientPid, int sequence, byte modifyType)
    {
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(x => x.RecipientPid == recipientPid && x.Sequence == sequence);
            if (message == null) return false;
            if (modifyType == 0) message.Read = true;
            // Mail with unclaimed CR or items cannot be deleted (the retail UI disables it too).
            else if (modifyType == 1)
            {
                if (message.CR > 0 || (message.Attachments?.Any(x => !x.Claimed) ?? false)) return false;
                _messages.Remove(message);
            }
            else return false;
            Save();
            return true;
        }
    }

    public bool TakeCR(int recipientPid, int sequence)
    {
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(x => x.RecipientPid == recipientPid && x.Sequence == sequence);
            if (message == null || message.CR <= 0) return false;
            message.CR = 0;
            Save();
            return true;
        }
    }

    // Claim only after PlayerStore accepts the item. The caller serializes mail operations, so an
    // attachment cannot be delivered twice between Peek and Claim.
    public TransmitContentItemInfo? PeekAttachment(int recipientPid, int messageSequence, int itemSequence)
    {
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(x => x.RecipientPid == recipientPid && x.Sequence == messageSequence);
            var attachment = message?.Attachments?.FirstOrDefault(x => x.Sequence == itemSequence && !x.Claimed);
            return attachment == null ? null : new TransmitContentItemInfo
            {
                Sequence = attachment.Sequence,
                Item = CloneItem(attachment.Item),
                Claimed = attachment.Claimed
            };
        }
    }

    public bool ClaimAttachment(int recipientPid, int messageSequence, int itemSequence)
    {
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(x => x.RecipientPid == recipientPid && x.Sequence == messageSequence);
            var attachment = message?.Attachments?.FirstOrDefault(x => x.Sequence == itemSequence && !x.Claimed);
            if (attachment == null) return false;
            attachment.Claimed = true;
            Save();
            return true;
        }
    }

    // HuxleySubsystem.ExpirationItem covers message items as well as inventory/warehouse.
    // GenericItem+12 is an absolute expiration timestamp for non-armor items; armor uses the
    // same field for spray/color data, matching PlayerStore's union-field guard.
    public List<ExpiredAttachment> PruneExpiredAttachments(int recipientPid,
        int? unixSeconds = null)
    {
        lock (_lock)
        {
            int now = unixSeconds ?? checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var expired = new List<ExpiredAttachment>();
            foreach (var message in _messages.Where(x => x.RecipientPid == recipientPid))
            {
                foreach (var attachment in message.Attachments.Where(x => !x.Claimed).ToArray())
                {
                    var item = attachment.Item;
                    if (item.LimitTime <= 0 || item.LimitTime > now
                        || (_itemCatalog.Get(item.ItemId)?.ArmorPart.Length ?? 0) != 0)
                        continue;

                    expired.Add(new ExpiredAttachment(message.Sequence, attachment.Sequence,
                        item.ItemId, item.TuneUpOptions, 0));
                    message.Attachments.Remove(attachment);
                }
            }

            if (expired.Count != 0) Save();
            return expired;
        }
    }

    private static PlayerStore.GenericItem CloneItem(PlayerStore.GenericItem item) => new()
    {
        InventoryIndex = item.InventoryIndex,
        InventoryCell = item.InventoryCell,
        ItemId = item.ItemId,
        Count = item.Count,
        State = item.State,
        LimitTime = item.LimitTime,
        TuneUpOptions = item.TuneUpOptions,
        Durability = item.Durability,
        Skill = item.Skill,
        Skill1 = item.Skill1
    };

    public void DeleteCharacter(int pid)
    {
        lock (_lock)
        {
            // Mail sent by the deleted character stays with its recipients; only the sender PID
            // is cleared so replies cannot reach a later owner of that PID.
            _messages.RemoveAll(x => x.RecipientPid == pid);
            foreach (var message in _messages.Where(x => x.SenderPid == pid))
                message.SenderPid = 0;
            Save();
        }
    }
}
