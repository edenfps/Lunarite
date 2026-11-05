using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;

namespace Lunarite.Storage;

public sealed class PayItemStore
{
    private readonly ILogger _logger;

    public sealed class StoredItem
    {
        public int StorageSeq { get; set; }

        public int StorageItemSeq { get; set; }

        public int ItemId { get; set; }

        public int ProductSeq { get; set; }

        public int PriceSeq { get; set; }

        public string CashName { get; set; } = "Free";

        public string SenderAccountId { get; set; } = "Private Server";
    }

    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "payitems.json");
    private readonly object _lock = new();
    private readonly Dictionary<int, List<StoredItem>> _items;

    private readonly AtomicJson _atomicJson;

    public PayItemStore(ILogger<PayItemStore> logger, AtomicJson atomicJson)
    {
        _logger = logger;
        _atomicJson = atomicJson;
        _items = Load();
    }

    private Dictionary<int, List<StoredItem>> Load()
    {
        try { return _atomicJson.Read<Dictionary<int, List<StoredItem>>>(_path) ?? []; }
        catch (Exception ex) { _logger.LogWarning("[PayItems] load failed: {0}", ex.Message); return []; }
    }

    private void Save() => _atomicJson.Write(_path,
        JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));

    public StoredItem Add(int accountId, CashShopCatalog.Entry entry,
        string senderAccountId = "Private Server")
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(accountId, out var list)) list = _items[accountId] = [];
            int sequence = _items.Values.SelectMany(x => x).Select(x => x.StorageSeq).DefaultIfEmpty().Max() + 1;
            var stored = new StoredItem
            {
                StorageSeq = sequence,
                StorageItemSeq = 1,
                ItemId = entry.ItemId,
                ProductSeq = entry.ItemId,
                PriceSeq = entry.ItemId,
                CashName = entry.Name,
                SenderAccountId = senderAccountId
            };
            list.Add(stored);
            Save();
            return stored;
        }
    }

    public IReadOnlyList<StoredItem> Get(int accountId)
    {
        lock (_lock) return _items.GetValueOrDefault(accountId)?.Select(Clone).ToArray() ?? [];
    }

    public bool TryTake(int accountId, int storageSeq, int storageItemSeq, out StoredItem item)
    {
        lock (_lock)
        {
            item = new();
            if (!_items.TryGetValue(accountId, out var list)) return false;
            var found = list.FirstOrDefault(x => x.StorageSeq == storageSeq && x.StorageItemSeq == storageItemSeq);
            if (found == null) return false;
            item = Clone(found);
            list.Remove(found);
            Save();
            return true;
        }
    }

    public void Restore(int accountId, StoredItem item)
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(accountId, out var list)) list = _items[accountId] = [];
            if (!list.Any(x => x.StorageSeq == item.StorageSeq && x.StorageItemSeq == item.StorageItemSeq))
                list.Add(Clone(item));
            Save();
        }
    }

    private static StoredItem Clone(StoredItem x) => new()
    {
        StorageSeq = x.StorageSeq, StorageItemSeq = x.StorageItemSeq, ItemId = x.ItemId,
        ProductSeq = x.ProductSeq, PriceSeq = x.PriceSeq, CashName = x.CashName,
        SenderAccountId = x.SenderAccountId
    };
}
