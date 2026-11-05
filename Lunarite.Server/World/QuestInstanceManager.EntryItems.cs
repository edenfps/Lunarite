using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Lunarite.World;

// Mission entry items taken from every party member when a room is created and held until the
// room goes live (or given back if it never does).
public sealed partial class QuestInstanceManager
{
    internal sealed class QuestEntryReservation
    {
        public List<int> Members = [];
        public List<(int ItemId, int Quantity)> Items = [];
    }

    private readonly ConcurrentDictionary<int, QuestEntryReservation> _questEntryReservations = [];

    public static int FindQuestEntryReservationHost(int memberPid,
        IEnumerable<(int HostPid, IReadOnlyCollection<int> Members)> reservations)
    {
        if (memberPid <= 0) return 0;
        foreach (var reservation in reservations)
            if (reservation.Members.Contains(memberPid)) return reservation.HostPid;
        return 0;
    }

    internal bool TryTakeQuestEntryReservationForMember(int memberPid,
        out int hostPid, out QuestEntryReservation? reservation)
    {
        hostPid = FindQuestEntryReservationHost(memberPid,
            _questEntryReservations.Select(x =>
                (x.Key, (IReadOnlyCollection<int>)x.Value.Members)));
        if (hostPid <= 0 || !_questEntryReservations.TryRemove(hostPid, out var removed))
        {
            reservation = null;
            return false;
        }
        reservation = removed;
        return true;
    }

    internal void SetEntryReservation(int hostPid, QuestEntryReservation reservation) =>
        _questEntryReservations[hostPid] = reservation;

    public static bool ShouldCommitQuestEntryReservation(
        QuestInstanceManager.QuestTransportSignal signal, bool roomNoticeAuthorized) =>
        signal is QuestInstanceManager.QuestTransportSignal.HostRoomLive or QuestInstanceManager.QuestTransportSignal.GatewayServerReady
        && roomNoticeAuthorized;

    // A solo host's op199 comes from a 30-second timer, so loot and settlement can arrive before
    // the room is confirmed. The first accepted reward report proves that participant is in the
    // running world: its entry items are spent and are no longer refunded if the room is abandoned.
    internal void CommitQuestEntryOnReward(int instanceToken, int pid)
    {
        int hostPid = HostOf(instanceToken);
        if (hostPid <= 0 || pid <= 0 || !_questEntryReservations.TryGetValue(hostPid, out var reservation)) return;
        lock (reservation)
            if (!reservation.Members.Remove(pid)) return;
        SaveQuestEntryReservation(hostPid, reservation);
    }

    // A party guest left before the room went live: only its own entry items come back, and it
    // leaves the pending transport so the host's room carries on without it.
    internal bool AbandonPendingQuestGuest(int memberPid)
    {
        if (memberPid <= 0) return false;
        bool found = false;
        foreach (var (hostPid, transport) in _questTransports)
        {
            if (hostPid == memberPid) continue;
            lock (transport)
            {
                if (!transport.Members.Remove(memberPid)) continue;
                transport.MemberPermissions.RemoveAll(permission => permission.Pid == memberPid);
            }
            found = true;
        }
        int reservationHost = FindQuestEntryReservationHost(memberPid,
            _questEntryReservations.Select(x => (x.Key, (IReadOnlyCollection<int>)x.Value.Members)));
        if (reservationHost > 0 && reservationHost != memberPid)
        {
            RefundQuestGuestEntry(reservationHost, memberPid, "left before the room went live");
            found = true;
        }
        return found;
    }

    internal bool TryTakeQuestEntryReservation(int hostPid, out QuestEntryReservation? reservation) =>
        _questEntryReservations.TryRemove(hostPid, out reservation);

    private void SaveQuestEntryReservation(int hostPid, QuestEntryReservation reservation)
    {
        if (reservation.Members.Count == 0)
        {
            _questEntryReservations.TryRemove(hostPid, out _);
            _questEntryReservationStore.Remove(hostPid);
        }
        else _questEntryReservationStore.Put(hostPid, reservation.Members, reservation.Items);
    }
}
