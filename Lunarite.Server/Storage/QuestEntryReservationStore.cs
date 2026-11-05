using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

// Quest entry items are consumed from every party member when a quest room is created and are
// only committed once the host's room is live. The pending reservation used to live in memory, so
// a server crash in that window destroyed the items. It is now written in the same
// MultiStoreTransaction as the consumption and removed on commit or refund; whatever survives a
// restart was never committed and is refunded at startup.
public sealed class QuestEntryReservationStore
{
    private readonly ILogger _logger;
    private readonly PlayerStore _playerStore;

    public sealed record Reservation(List<int> Members, List<Item> Items);

    public sealed record Item(int ItemId, int Quantity);

    private static readonly object Lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "questentryreservations.json");
    private Dictionary<int, Reservation> _data;

    private readonly AtomicJson _atomicJson;
    private readonly MultiStoreTransaction _multiStoreTransaction;

    public QuestEntryReservationStore(ILogger<QuestEntryReservationStore> logger, PlayerStore playerStore,
        AtomicJson atomicJson, MultiStoreTransaction multiStoreTransaction)
    {
        _logger = logger;
        _playerStore = playerStore;
        _atomicJson = atomicJson;
        _multiStoreTransaction = multiStoreTransaction;
        _data = Load();
    }

    private Dictionary<int, Reservation> Load()
    {
        try { return _atomicJson.Read<Dictionary<int, Reservation>>(PathName) ?? []; }
        catch (Exception ex) { _logger.LogWarning("[QuestEntry] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save() =>
        _atomicJson.Write(PathName, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));

    public void Put(int hostPid, IEnumerable<int> members, IEnumerable<(int ItemId, int Quantity)> items)
    {
        lock (Lock)
        {
            _data[hostPid] = new Reservation(members.Distinct().ToList(),
                items.Select(x => new Item(x.ItemId, x.Quantity)).ToList());
            Save();
        }
    }

    public void Remove(int hostPid)
    {
        lock (Lock)
        {
            if (_data.Remove(hostPid)) Save();
        }
    }

    public IReadOnlyDictionary<int, Reservation> All()
    {
        lock (Lock) return new Dictionary<int, Reservation>(_data);
    }

    // Refund every reservation left over from a previous process (never committed), one atomic
    // transaction per reservation. Returns the number of member refunds performed.
    public int RefundAbandoned()
    {
        int refunds = 0;
        foreach (var (hostPid, reservation) in All())
        {
            _multiStoreTransaction.Run(() =>
            {
                foreach (int memberPid in reservation.Members)
                {
                    if (_playerStore.TryExchangeQuestItems(memberPid, [],
                            reservation.Items.Select(x => (x.ItemId, x.Quantity)).ToList(), out _, out _))
                        refunds++;
                    else
                        _logger.LogError("[QuestEntry] could not refund reserved entry items to PID {0} (host {1}).",
                            memberPid, hostPid);
                }
                Remove(hostPid);
            });
            _logger.LogWarning("[QuestEntry] refunded uncommitted entry items for host PID {0} to [{1}].",
                hostPid, string.Join(",", reservation.Members));
        }
        return refunds;
    }

    internal void ResetForTest()
    {
        lock (Lock) { _data = []; Save(); }
    }
}
