using System;
using System.Collections.Generic;
using System.Linq;

namespace Lunarite.Resources;

// op56/op57 award temporary world-pickup entitlements. The rally-end menu later submits
// op47/op48 to move only collected pickups into persistent inventory. Keep a per-run upper
// bound so a replay or forged menu request cannot create additional items.
public static class QuestLootGrants
{
    private static readonly object Lock = new();
    private static readonly Dictionary<(int Instance, int Pid), Dictionary<int, int>> Pending = [];

    public static void Issue(int instance, int pid, IEnumerable<(int ItemId, int Quantity)> drops)
    {
        if (instance <= 0 || pid <= 0 || drops == null) return;
        lock (Lock)
        {
            var key = (instance, pid);
            foreach (var (itemId, quantity) in drops)
            {
                if (itemId <= 0 || quantity <= 0) continue;
                if (!Pending.TryGetValue(key, out var items))
                    items = Pending[key] = [];
                items[itemId] = (int)Math.Min(int.MaxValue,
                    (long)items.GetValueOrDefault(itemId) + quantity);
            }
        }
    }

    // The inventory commit runs under the same lock as the entitlement check. A failed commit
    // leaves grants available for retry; a successful commit consumes each quantity once.
    public static bool TryClaim(int instance, int pid,
        IEnumerable<(int ItemId, int Quantity)> requested, Func<bool> commit)
    {
        if (commit == null) return false;
        return TryClaimUpTo(instance, pid, requested, clamp: false,
            entitled => commit() ? entitled.Select(x => x.Quantity).ToArray() : null, out _);
    }

    // Partial form for the rally-end menu: commit gets the entitled quantity per item (grouped, in
    // request order) and returns how many units of each it inserted, or null on failure. Only the
    // inserted units are consumed, so a full inventory leaves the rest claimable. With clamp, each
    // item is capped at its issued quantity instead of failing the whole request.
    public static bool TryClaimUpTo(int instance, int pid,
        IEnumerable<(int ItemId, int Quantity)> requested, bool clamp,
        Func<IReadOnlyList<(int ItemId, int Quantity)>, IReadOnlyList<int>?> commit,
        out List<(int ItemId, int Entitled, int Inserted)> results)
    {
        results = [];
        if (instance <= 0 || pid <= 0 || requested == null || commit == null) return false;
        var claims = requested.GroupBy(x => x.ItemId)
            .Select(group => (ItemId: group.Key, Quantity: group.Sum(x => (long)x.Quantity)))
            .ToArray();
        if (claims.Length == 0) return false;
        if (claims.Any(x => x.ItemId <= 0 || x.Quantity <= 0))
        {
            if (!clamp) return false;
            claims = claims.Where(x => x.ItemId > 0 && x.Quantity > 0).ToArray();
        }
        lock (Lock)
        {
            var key = (instance, pid);
            if (!Pending.TryGetValue(key, out var items)
                || !clamp && claims.Any(x => x.Quantity > items.GetValueOrDefault(x.ItemId))) return false;
            var entitled = claims
                .Select(x => (x.ItemId, Quantity: (int)Math.Min(x.Quantity, items.GetValueOrDefault(x.ItemId))))
                .Where(x => x.Quantity > 0).ToList();
            if (entitled.Count == 0) return false;
            var inserted = commit(entitled);
            if (inserted == null || inserted.Count != entitled.Count) return false;
            for (int i = 0; i < entitled.Count; i++)
            {
                var (itemId, quantity) = entitled[i];
                int used = Math.Clamp(inserted[i], 0, quantity);
                results.Add((itemId, quantity, used));
                if (used == 0) continue;
                int left = items[itemId] - used;
                if (left == 0) items.Remove(itemId);
                else items[itemId] = left;
            }
            if (items.Count == 0) Pending.Remove(key);
            return true;
        }
    }

    public static void ClearParticipant(int instance, int pid)
    {
        lock (Lock) Pending.Remove((instance, pid));
    }

    public static void ClearInstance(int instance)
    {
        lock (Lock)
            foreach (var key in Pending.Keys.Where(key => key.Instance == instance).ToArray())
                Pending.Remove(key);
    }
}
