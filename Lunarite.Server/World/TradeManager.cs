using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class TradeManager
{

    private readonly BlockStore _blockStore;
    private readonly CharacterStore _characterStore;
    private readonly PlayerManager _playerManager;
    private readonly PlayerStore _playerStore;

    internal sealed class TradeSession
    {
        public int A;
        public int B;
        public Dictionary<byte, byte> AOffers = [];
        public Dictionary<byte, byte> BOffers = [];
        // Trade slot -> the item the partner was shown; the commit refuses a cell that changed since.
        public Dictionary<byte, PlayerStore.GenericItem> AOffered = [];
        public Dictionary<byte, PlayerStore.GenericItem> BOffered = [];
        public int AMoney;
        public int BMoney;
        public bool AConfirmed;
        public bool BConfirmed;
    }

    private readonly object _tradeLock = new();
    private readonly Dictionary<int, int> _tradeRequests = []; // target PID -> requesting PID
    private readonly Dictionary<int, TradeSession> _trades = []; // both PIDs -> same session

    private readonly MultiStoreTransaction _multiStoreTransaction;

    private readonly QuestRoomMembers _questRoomMembers;

    private readonly ItemManager _itemManager;

    public TradeManager(BlockStore blockStore, CharacterStore characterStore, PlayerManager playerManager,
        PlayerStore playerStore, MultiStoreTransaction multiStoreTransaction, QuestRoomMembers questRoomMembers, ItemManager itemManager)
    {
        _itemManager = itemManager;
        _questRoomMembers = questRoomMembers;
        _blockStore = blockStore;
        _characterStore = characterStore;
        _playerManager = playerManager;
        _playerStore = playerStore;
        _multiStoreTransaction = multiStoreTransaction;
    }

    private Client? OnlineClient(int pid) => _playerManager.FindClient(pid);

    private string CharacterName(int pid) => _characterStore.Get(pid)?.Name ?? "";

    private static int TradePartner(TradeSession trade, int pid) => pid == trade.A ? trade.B : trade.A;

    private bool CanTradeWith(Client client, int pid, int otherPid)
    {
        var other = OnlineClient(otherPid);
        return other != null && IsPlayerTradePresenceValid(
            Game.PlayerFor(pid).IsSpawned, Game.PlayerFor(otherPid).IsSpawned,
            _questRoomMembers.InstanceOf(client.Id),
            _questRoomMembers.InstanceOf(other.Id),
            (Game.PlayerFor(pid).CityMap ?? 0), (Game.PlayerFor(otherPid).CityMap ?? 0));
    }

    // Action 0: ask the target; 4 when unavailable, 11 when the target blocks the requester.
    private void TradeAskLocked(Client client, int pid, int target)
    {
        var targetClient = OnlineClient(target);
        if (target == pid || targetClient == null || !CanTradeWith(client, pid, target)
            || !IsTradeRequestAvailable(pid, target, _tradeRequests, _trades.Keys))
        {
            client.SendPacket(new Trade { Action = TradeAction.Busy });
            return;
        }
        if (_blockStore.List(target).Contains(pid))
        {
            client.SendPacket(new Trade { Action = TradeAction.BlockPlayer });
            return;
        }
        _tradeRequests[target] = pid;
        targetClient.SendPacket(new Trade { Action = TradeAction.Ask, Pid = pid, Name = CharacterName(pid) });
    }

    // Action 1: accept the pending request and open the session.
    private void TradeAcceptLocked(Client client, int pid, int requester)
    {
        if (!IsTradeAcceptanceAvailable(pid, requester, _tradeRequests, _trades.Keys))
        {
            client.SendPacket(new TradeFail());
            return;
        }
        _tradeRequests.Remove(pid);
        if (!CanTradeWith(client, pid, requester) || OnlineClient(requester) is not Client requesterClient)
        {
            client.SendPacket(new TradeFail());
            return;
        }
        var trade = new TradeSession { A = requester, B = pid };
        _trades[requester] = trade; _trades[pid] = trade;
        requesterClient.SendPacket(new Trade { Action = TradeAction.Accept, Pid = pid, Name = CharacterName(pid) });
    }

    // Action 5: put a MAIN cell in a trade slot (answer 0, partner sees 1, refusal 2).
    private void TradeAddItemLocked(Client client, int pid, byte slot, byte mainCell)
    {
        if (!_trades.TryGetValue(pid, out var trade)) { client.SendPacket(new TradeFail()); return; }
        var item = _playerStore.GetInventory(pid, () => [])
            .FirstOrDefault(x => x.InventoryIndex == InventoryType.Main && x.InventoryCell == mainCell);
        var offers = pid == trade.A ? trade.AOffers : trade.BOffers;
        if (item == null || !_playerStore.CanPlayerTradeItem(item)
            || slot >= PlayerStore.TradeSlotCapacity
            || offers.ContainsKey(slot) || offers.ContainsValue(mainCell))
        {
            client.SendPacket(new Trade { Action = TradeAction.AddItem, Result = TradeItemResult.Refused });
            return;
        }
        offers[slot] = mainCell; ResetTradeConfirmation(trade);
        (pid == trade.A ? trade.AOffered : trade.BOffered)[slot] = ItemManager.CloneInventoryItem(item);
        client.SendPacket(new Trade { Action = TradeAction.AddItem, Result = TradeItemResult.Self });
        OnlineClient(TradePartner(trade, pid))?.SendPacket(new Trade
            { Action = TradeAction.AddItem, Result = TradeItemResult.Partner, Slot = slot, Item = ItemManager.SerializeGenericItem(item) });
    }

    // Action 6: take an item back out of a trade slot.
    private void TradeSubItemLocked(Client client, int pid, byte slot)
    {
        if (!_trades.TryGetValue(pid, out var trade)) { client.SendPacket(new TradeFail()); return; }
        var offers = pid == trade.A ? trade.AOffers : trade.BOffers;
        (pid == trade.A ? trade.AOffered : trade.BOffered).Remove(slot);
        if (!offers.Remove(slot))
        {
            client.SendPacket(new Trade { Action = TradeAction.SubItem, Result = TradeItemResult.Refused });
            return;
        }
        ResetTradeConfirmation(trade);
        client.SendPacket(new Trade { Action = TradeAction.SubItem, Result = TradeItemResult.Self });
        OnlineClient(TradePartner(trade, pid))?.SendPacket(new Trade
        {
            Action = TradeAction.SubItem, Result = TradeItemResult.Partner, Slot = slot
        });
    }

    // Action 7: set the offered money.
    private void TradeSetMoneyLocked(Client client, int pid, int money)
    {
        if (!_trades.TryGetValue(pid, out var trade) || money < 0 || _playerStore.GetProgress(pid).Money < money)
        {
            client.SendPacket(new TradeFail());
            return;
        }
        if (pid == trade.A) trade.AMoney = money; else trade.BMoney = money;
        ResetTradeConfirmation(trade);
        OnlineClient(TradePartner(trade, pid))?.SendPacket(new Trade { Action = TradeAction.SetMoney, Money = money });
    }

    // Action 8: confirm; the second confirmation commits the exchange.
    private void TradeAskCompletionLocked(Client client, int pid)
    {
        if (!_trades.TryGetValue(pid, out var trade)) { client.SendPacket(new TradeFail()); return; }
        if (!CanTradeWith(client, pid, TradePartner(trade, pid)))
        {
            _trades.Remove(trade.A); _trades.Remove(trade.B);
            OnlineClient(trade.A)?.SendPacket(new Trade { Action = TradeAction.Fail });
            OnlineClient(trade.B)?.SendPacket(new Trade { Action = TradeAction.Fail });
            return;
        }
        if (pid == trade.A) trade.AConfirmed = true; else trade.BConfirmed = true;
        OnlineClient(TradePartner(trade, pid))?.SendPacket(new Trade { Action = TradeAction.AskCompletion });
        if (trade.AConfirmed && trade.BConfirmed) CommitTradeLocked(trade);
    }

    // Swaps both offers atomically, then publishes each side's changed cells and money. Inventory
    // full answers 13 to the side without room and 14 to the other; any other failure is 3.
    private void CommitTradeLocked(TradeSession trade)
    {
        var aBefore = _playerStore.GetInventory(trade.A, () => []).Select(ItemManager.CloneInventoryItem).ToArray();
        var bBefore = _playerStore.GetInventory(trade.B, () => []).Select(ItemManager.CloneInventoryItem).ToArray();
        PlayerStore.TradeCommitResult committed = PlayerStore.TradeCommitResult.Invalid;
        _multiStoreTransaction.Run(() =>
        {
            var aItems = _playerStore.GetInventory(trade.A, () => [])
                .Where(item => item.InventoryIndex == InventoryType.Main && trade.AOffers.Values.Contains(item.InventoryCell))
                .Select(item => item.ItemId).ToArray();
            var bItems = _playerStore.GetInventory(trade.B, () => [])
                .Where(item => item.InventoryIndex == InventoryType.Main && trade.BOffers.Values.Contains(item.InventoryCell))
                .Select(item => item.ItemId).ToArray();
            committed = _playerStore.CompleteTrade(
                trade.A, trade.AOffers, trade.AMoney, trade.B, trade.BOffers, trade.BMoney,
                trade.AOffered, trade.BOffered);
            if (committed == PlayerStore.TradeCommitResult.Success)
            {
                _itemManager.SynchronizeQuestPickupObjectives(trade.A, aItems.Concat(bItems));
                _itemManager.SynchronizeQuestPickupObjectives(trade.B, aItems.Concat(bItems));
            }
        });
        _trades.Remove(trade.A); _trades.Remove(trade.B);
        switch (committed)
        {
            case PlayerStore.TradeCommitResult.Success:
                foreach (var (traderPid, before) in new[] { (trade.A, aBefore), (trade.B, bBefore) })
                {
                    if (OnlineClient(traderPid) is not Client traderClient) continue;
                    var after = _playerStore.GetInventory(traderPid, () => []);
                    long money = _playerStore.GetProgress(traderPid).Money;
                    foreach (var update in BuildTradeSettlementPackets(before, after, money))
                        traderClient.SendPacket(update);
                }
                break;
            case PlayerStore.TradeCommitResult.AInventoryFull:
                OnlineClient(trade.A)?.SendPacket(new Trade { Action = TradeAction.InvenBlocked });
                OnlineClient(trade.B)?.SendPacket(new Trade { Action = TradeAction.AnotherPlayerInvenBlocked });
                break;
            case PlayerStore.TradeCommitResult.BInventoryFull:
                OnlineClient(trade.A)?.SendPacket(new Trade { Action = TradeAction.AnotherPlayerInvenBlocked });
                OnlineClient(trade.B)?.SendPacket(new Trade { Action = TradeAction.InvenBlocked });
                break;
            default:
                var failed = new Trade { Action = TradeAction.Fail };
                OnlineClient(trade.A)?.SendPacket(failed); OnlineClient(trade.B)?.SendPacket(failed);
                break;
        }
    }

    // Action 10: withdraw a confirmation.
    private void TradeCompletionCancelLocked(int pid)
    {
        if (!_trades.TryGetValue(pid, out var trade)) return;
        if (pid == trade.A) trade.AConfirmed = false; else trade.BConfirmed = false;
        OnlineClient(TradePartner(trade, pid))?.SendPacket(new Trade { Action = TradeAction.CompletionCancel });
    }

    // Trade targets a visible city player and native callbacks require MMO state 2. Checked on
    // accept and commit, since either side can travel while the menu is open. Quest instances
    // do not count.
    public static bool IsPlayerTradePresenceValid(bool spawnedA, bool spawnedB,
        int instanceA, int instanceB, int mapA, int mapB) =>
        spawnedA && spawnedB && instanceA == 0 && instanceB == 0 && mapA == mapB;

    // One pending or active exchange per PID, requester included; otherwise two recipients could
    // accept separate prompts and overwrite the first TradeSession.
    public static bool IsTradeRequestAvailable(int requester, int target,
        IReadOnlyDictionary<int, int> pending, ICollection<int> active) =>
        requester > 0 && target > 0 && requester != target
        && !active.Contains(requester) && !active.Contains(target)
        && !pending.ContainsKey(requester) && !pending.ContainsKey(target)
        && !pending.Values.Contains(requester) && !pending.Values.Contains(target);

    public static bool IsTradeAcceptanceAvailable(int acceptor, int requester,
        IReadOnlyDictionary<int, int> pending, ICollection<int> active) =>
        pending.TryGetValue(acceptor, out int expected) && expected == requester
        && !active.Contains(acceptor) && !active.Contains(requester);

    private void ResetTradeConfirmation(TradeSession trade)
    {
        if (!trade.AConfirmed && !trade.BConfirmed) return;
        trade.AConfirmed = trade.BConfirmed = false;
        _playerManager.FindClient(trade.A)?.SendPacket(new Trade { Action = TradeAction.CompletionCancel });
        _playerManager.FindClient(trade.B)?.SendPacket(new Trade { Action = TradeAction.CompletionCancel });
    }

    public static IReadOnlyList<int> TradeCancellationRecipients(int pid,
        IReadOnlyDictionary<int, int> pending, int activeCounterpart)
    {
        var recipients = new HashSet<int>();
        if (pending.TryGetValue(pid, out int requester) && requester > 0 && requester != pid)
            recipients.Add(requester);
        foreach (var (target, requestor) in pending)
            if (requestor == pid && target > 0 && target != pid)
                recipients.Add(target);
        if (activeCounterpart > 0 && activeCounterpart != pid)
            recipients.Add(activeCounterpart);
        return recipients.Order().ToArray();
    }

    private void TradeCancelLocked(int pid, byte notifyAction)
    {
        int activeCounterpart = _trades.TryGetValue(pid, out var trade)
            ? pid == trade.A ? trade.B : trade.A : 0;
        var recipients = TradeCancellationRecipients(pid, _tradeRequests, activeCounterpart);
        _tradeRequests.Remove(pid);
        foreach (int target in _tradeRequests.Where(x => x.Value == pid).Select(x => x.Key).ToList())
            _tradeRequests.Remove(target);
        if (trade != null)
        {
            _trades.Remove(trade.A); _trades.Remove(trade.B);
        }
        foreach (int recipient in recipients)
            _playerManager.FindClient(recipient)?.SendPacket(new Trade { Action = notifyAction, Pid = pid });
    }

    // OnTradeCompletion closes the menu without touching client items or money, so changed MAIN
    // cells are sent here. A cell that held the offered item and now holds the received one goes
    // out as RemoveItem + InsertItem: op217 there left the cell empty on screen until relog
    // (live 2026-11-05, both traders), though the full record still travels in the insert.
    public static IReadOnlyList<IPacketSerialize> BuildTradeSettlementPackets(
        IEnumerable<PlayerStore.GenericItem> beforeItems,
        IEnumerable<PlayerStore.GenericItem> afterItems, long money) =>
        [new Trade { Action = TradeAction.Completion },
         .. ItemManager.BuildInventorySlotChanges(beforeItems, afterItems, InventoryType.Main)
             .SelectMany(change => change is ItemReplacement replacement
                 ? new IPacketSerialize[] { new RemoveItem
                     { InventoryIndex = replacement.RemovedInventory, Cell = replacement.RemovedCell }, replacement.Item }
                 : [change]),
         new Parameter { Param = Parameter.PARAMETER_MONEY,
             Value = (int)Math.Clamp(money, int.MinValue, int.MaxValue) }];

    internal void CancelTradeOnLeave(int pid)
    {
        lock (_tradeLock)
            TradeCancelLocked(pid, _trades.ContainsKey(pid) ? TradeAction.Cancel : TradeAction.CancelAsk);
    }

    // Client op85 carries the same action codes the server answers with (TradeAction).
    internal void Trade(Client client, int pid, byte action, int argument, byte first, byte second)
    {
        lock (_tradeLock)
        {
            switch (action)
            {
                case TradeAction.Ask: TradeAskLocked(client, pid, target: argument); break;
                case TradeAction.Accept: TradeAcceptLocked(client, pid, requester: argument); break;
                case TradeAction.Cancel: TradeCancelLocked(pid, notifyAction: TradeAction.Cancel); break;
                case TradeAction.AddItem: TradeAddItemLocked(client, pid, slot: first, mainCell: second); break;
                case TradeAction.SubItem: TradeSubItemLocked(client, pid, slot: first); break;
                case TradeAction.SetMoney: TradeSetMoneyLocked(client, pid, money: argument); break;
                case TradeAction.AskCompletion: TradeAskCompletionLocked(client, pid); break;
                case TradeAction.CompletionCancel: TradeCompletionCancelLocked(pid); break;
                case TradeAction.CancelAsk: TradeCancelLocked(pid, notifyAction: TradeAction.CancelAsk); break;
            }
        }
    }
}
