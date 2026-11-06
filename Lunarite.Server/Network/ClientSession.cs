using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;

using Lunarite.Packets.Incoming;

namespace Lunarite.Network;

// State that belongs to one connection and ends with it: the authenticated account, the
// client's P2P reports, travel and purchase handshakes, and the menus it is on. Held by Client,
// so nothing has to clean it up on disconnect.
public sealed class ClientSession
{
    private readonly object _lock = new();

    // Account the Agent login (or the Game link's handoff ticket) authenticated.
    public int? AccountId;

    // The Agent login is on HUXLEY_GM_LOGINS (a full server still lets it in).
    public bool IsGm;

    // Address the client reported for itself in Connect (op0); used to address P2P hosts that
    // share the server's NAT.
    public IPAddress? LocalAddress;

    // P2P reports: NAT endpoints (op155), socket addresses (op196) and the hole-punch test
    // acknowledgment (op118), which a guest's join waits for on another thread.
    public P2PNatEndpointReport? NatEndpoint;
    public P2PNetworkAddressesReport? NetworkAddresses;
    private volatile bool _holePunchAcknowledged;

    public bool HolePunchAcknowledged
    {
        get => _holePunchAcknowledged;
        set => _holePunchAcknowledged = value;
    }

    // Set when this link is about to drop for quest travel (op42/op52) or a city map change,
    // so the disconnect keeps the character's quest room and squad.
    public bool ExpectedQuestTravel;
    public long? ExpectedCityTravelAt; // Unix ms

    // Mission and battle wear reaches the server while the client is away; its city copy of the gear
    // keeps the old durability until the next Spawn sends it.
    private int _equipmentRefresh;
    public void QueueEquipmentRefresh() => Volatile.Write(ref _equipmentRefresh, 1);
    public bool TryTakeEquipmentRefresh() => Interlocked.Exchange(ref _equipmentRefresh, 0) != 0;

    // Tutorial quest that starts once the character has spawned.
    public int? PendingTutorialQuest;

    // Drink setter bought with op204 and not yet consumed by its op202 gauge-bar result.
    public int? PendingDrinkSetter;

    public bool TryTakePendingDrink(out int setterId)
    {
        setterId = PendingDrinkSetter ?? 0;
        bool pending = PendingDrinkSetter.HasValue;
        PendingDrinkSetter = null;
        return pending;
    }

    // op205 new-mail markers go out once per selection.
    private int _mailboxArrivalPublished;

    public bool TryPublishMailboxArrival() => Interlocked.Exchange(ref _mailboxArrivalPublished, 1) == 0;

    public void ResetMailboxArrival() => Interlocked.Exchange(ref _mailboxArrivalPublished, 0);

    // Quest list refreshes held back while the client travels (Game.QuestRefresh* flags).
    private byte? _pendingQuestRefresh;

    public void QueueQuestRefresh(byte flags)
    {
        lock (_lock) _pendingQuestRefresh = (byte)((_pendingQuestRefresh ?? 0) | flags);
    }

    public bool TryTakeQuestRefresh(out byte flags)
    {
        lock (_lock)
        {
            flags = _pendingQuestRefresh ?? 0;
            bool queued = _pendingQuestRefresh.HasValue;
            _pendingQuestRefresh = null;
            return queued;
        }
    }

    public void ClearQuestRefresh()
    {
        lock (_lock) _pendingQuestRefresh = null;
    }

    // Native op40 SendQuestEnter(nKey) answers server op44 (key, quest). A key is valid once,
    // for the connection and quest it was issued to.
    private (int Key, int QuestIndex)? _questEnterKey;

    public void SetQuestEnterKey(int key, int questIndex)
    {
        lock (_lock) _questEnterKey = (key, questIndex);
    }

    public bool TryConsumeQuestEnterKey(int key, int questIndex)
    {
        lock (_lock)
        {
            if (questIndex == 0 || _questEnterKey is not { } issued) return false;
            if (issued.Key != key || issued.QuestIndex != questIndex) return false;
            _questEnterKey = null;
            return true;
        }
    }

    public void ClearQuestEnterKey()
    {
        lock (_lock) _questEnterKey = null;
    }

    // Mail list page (8 rows, UIMMOCommunity.MsgListScene).
    public int TransmitPage;

    // Current page per auction list request opcode.
    public ConcurrentDictionary<byte, int> AuctionPages { get; } = [];

    // Current page and keyword per player-directory list kind (squad / LFS / player search).
    public ConcurrentDictionary<byte, int> DirectoryPages { get; } = [];

    public ConcurrentDictionary<byte, string> DirectorySearches { get; } = [];

    // AllowTextMessage flood control: send times in the last ten seconds and the block deadline.
    public Queue<long> ChatSentAt { get; } = new();

    public long ChatBlockedUntil;
}
