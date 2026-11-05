using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    // A private encrypted Game socket: the shared quest socket is closed by earlier sections.
    private sealed class AuditGameSocket : IDisposable
    {
        private readonly System.Net.Sockets.TcpClient _peer, _accepted;
        private readonly System.Net.Sockets.NetworkStream _stream;
        public readonly Client Client;

        public AuditGameSocket(int clientId, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            _peer = new System.Net.Sockets.TcpClient();
            _peer.Connect(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
            _accepted = listener.AcceptTcpClient();
            listener.Stop();
            Client = new Client(clientId, new Server(0, HandlerType.Game, static (_, _) => { }, loggerFactory), _accepted);
            _peer.ReceiveTimeout = 3000;
            _stream = _peer.GetStream();
            _ = Read(); // GameVersion, sent by the Client constructor.
        }

        public byte[] Read()
        {
            Span<byte> header = stackalloc byte[2];
            _stream.ReadExactly(header);
            var payload = new byte[System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header) - 2];
            _stream.ReadExactly(payload);
            for (int i = 0; i < payload.Length; i++) payload[i] = Client.DecryptTable[0, payload[i]];
            return payload;
        }

        public byte[] Request(byte[] request, byte replyOpcode)
        {
            Game.OnHandlePacket(Client, request);
            byte[] reply;
            do reply = Read(); while (reply[0] != replyOpcode);
            return reply;
        }

        public void Dispose()
        {
            Client.Dispose(); _accepted.Dispose(); _peer.Dispose();
        }
    }

    // Offers are bound to the item the partner was shown, not just the MAIN cell number.
    private void TradeOfferSnapshotBinding()
    {
        const int seller = 920600, buyer = 920601;
        ResetCharacter(seller); ResetCharacter(buyer);
        _playerStore.GetInventory(buyer, () => []);
        var seller5 = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 5, ItemId = 604221, Count = 3 };
        var seller20 = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 20, ItemId = 604222, Count = 1 };
        _playerStore.GetInventory(seller, () => [seller5, seller20]);
        var shown = new Dictionary<byte, PlayerStore.GenericItem> { [0] = ItemManager.CloneInventoryItem(seller5) };
        var offers = new Dictionary<byte, byte> { [0] = 5 };

        // Swap cells 5 and 20 after the partner saw cell 5.
        seller5.InventoryCell = 20; seller20.InventoryCell = 5;
        Check(_playerStore.CompleteTrade(seller, offers, 0, buyer, new Dictionary<byte, byte>(), 0, shown, null)
                == PlayerStore.TradeCommitResult.Invalid
            && _playerStore.GetInventory(buyer, () => []).Count == 0
            && _playerStore.GetInventory(seller, () => []).Count == 2,
            "trade refuses an offered MAIN cell swapped for another item after the partner saw it");
        seller5.InventoryCell = 5; seller20.InventoryCell = 20;

        // Split the offered stack down after the partner saw three.
        seller5.Count = 1;
        Check(_playerStore.CompleteTrade(seller, offers, 0, buyer, new Dictionary<byte, byte>(), 0, shown, null)
                == PlayerStore.TradeCommitResult.Invalid
            && _playerStore.GetInventory(buyer, () => []).Count == 0,
            "trade refuses an offered stack split after the partner saw its count");
        seller5.Count = 3;

        Check(_playerStore.CompleteTrade(seller, offers, 0, buyer, new Dictionary<byte, byte>(), 0, shown, null)
                == PlayerStore.TradeCommitResult.Success
            && _playerStore.GetInventory(buyer, () => []).Single() is { ItemId: 604221, Count: 3 },
            "trade commits an unchanged offered item exactly as shown");
    }

    private void MailSendLimits()
    {
        var lastSend = new Dictionary<int, long>();
        var dailySends = new Dictionary<int, (long Day, int Count)>();
        bool firstFree = !TransmitManager.IsTransmitSendThrottled(lastSend, 7, 10_000);
        TransmitManager.RecordTransmitSend(lastSend, dailySends, 7, 10_000, 1);
        Check(firstFree
            && TransmitManager.IsTransmitSendThrottled(lastSend, 7, 10_500)
            && !TransmitManager.IsTransmitSendThrottled(lastSend, 8, 10_500)
            && !TransmitManager.IsTransmitSendThrottled(lastSend, 7, 11_500),
            "player mail sends are throttled per sender");
        Check(TransmitManager.TransmitPageCount(0) == 0 && TransmitManager.TransmitPageCount(9) == 2
            && TransmitManager.TransmitPageCount(8 * 300) == byte.MaxValue,
            "op199 mailbox page count clamps to its byte field instead of wrapping");

        const int fullMailbox = 920610;
        _transmitStore.DeleteCharacter(fullMailbox);
        for (int i = 0; i < TransmitStore.MailboxCapacity - 1; i++)
            _transmitStore.Add(fullMailbox, 7, 1, "Player", "Filler", "");
        bool belowCap = !_transmitStore.IsMailboxFull(fullMailbox);
        _transmitStore.Add(fullMailbox, 7, 1, "Player", "Filler", "");
        Check(belowCap && _transmitStore.IsMailboxFull(fullMailbox),
            "player-sendable mail stops at the per-recipient mailbox cap");
        _transmitStore.DeleteCharacter(fullMailbox);

        // Two back-to-back production sends: the second inside the interval is refused untouched.
        using var socket = new AuditGameSocket(920691, _loggerFactory);
        socket.Client.Player = _playerManager.Get(_ownedB.Pid);
        int beforeMail = _transmitStore.List(_ownedA.Pid).Count;
        byte[] SendMail()
        {
            using var mail = new PacketWriter();
            mail.Write((byte)180); mail.Write(0); mail.Write(_ownedA.Name);
            mail.Write("Throttle"); mail.Write("body"); mail.Write(0);
            mail.Write(byte.MaxValue); mail.Write(byte.MaxValue); mail.Write(byte.MaxValue);
            return socket.Request(mail.Buffer, 201);
        }
        var firstReply = SendMail();
        var secondReply = SendMail();
        Check(firstReply[1] == TransmitResultCode.Ok && secondReply[1] == TransmitResultCode.Error
            && _transmitStore.List(_ownedA.Pid).Count == beforeMail + 1,
            "a mail flood inside the send interval answers TRANSMITRET_ERROR and stores nothing");
    }

    private void ClanInviteRevalidation()
    {
        const int leader = 920620, officer = 920621, firedTarget = 920622, demotedTarget = 920623;
        foreach (int pid in new[] { leader, officer, firedTarget, demotedTarget }) _clanStore.DeleteCharacter(pid);
        _clanStore.Leave(leader);
        Check(_clanStore.Create(leader, "Audit920620") == 0
            && _clanStore.Invite(leader, officer) == 7
            && _clanStore.AnswerInvite(officer, "Audit920620", true) == 6
            && _clanStore.Appoint(leader, officer, 3) == 33
            && _clanStore.SetPermissions(leader, [1, 0, 0, 0, 0, 0, 0, 0, 0, 0]) == 38
            && _clanStore.Invite(officer, demotedTarget) == 7
            && _clanStore.SetPermissions(leader, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]) == 38
            && _clanStore.AnswerInvite(demotedTarget, "Audit920620", true) == 10
            && _clanStore.ForPid(demotedTarget) == null,
            "an invitation from an officer who lost the invite permission no longer admits the target");
        Check(_clanStore.SetPermissions(leader, [1, 0, 0, 0, 0, 0, 0, 0, 0, 0]) == 38
            && _clanStore.Invite(officer, firedTarget) == 7
            && _clanStore.Fire(leader, officer) == 23
            && _clanStore.AnswerInvite(firedTarget, "Audit920620", true) == 10
            && _clanStore.ForPid(firedTarget) == null,
            "an invitation from a fired member no longer admits the target");
        _clanStore.Leave(leader);
    }

    // The shop is deferred: op197 open/Buy/Present fail, paid storage still releases GM gifts.
    private void PaidShopDeferred()
    {
        Check(!CashShopCatalog.ShopOpen, "the in-game cash shop is closed while it is deferred");
        using var socket = new AuditGameSocket(920692, _loggerFactory);
        socket.Client.Player = _playerManager.Get(_ownedB.Pid);
        socket.Client.Session.AccountId = _accountB;
        foreach (var row in _payItemStore.Get(_accountB))
            _payItemStore.TryTake(_accountB, row.StorageSeq, row.StorageItemSeq, out _);
        var package = _cashShopCatalog.Entries[0];
        byte[] Send(Action<PacketWriter> body)
        {
            using var request = new PacketWriter();
            request.Write((byte)197);
            body(request);
            return socket.Request(request.Buffer, 227);
        }
        var open = Send(w => w.Write(PayItemRequest.CanIOpenIngameshop));
        var buy = Send(w => { w.Write(PayItemRequest.Buy); w.Write(package.PackageId); w.Write(0); w.Write(0); w.Write((byte)0); });
        var present = Send(w =>
        {
            w.Write(PayItemRequest.PresentItem); w.Write(_ownedB.Name); w.Write("gift");
            w.Write(package.PackageId); w.Write(0); w.Write(0); w.Write((byte)0);
        });
        int storedAfterShop = _payItemStore.Get(_accountB).Count;
        var gift = _payItemStore.Add(_accountB, package, "GM");
        var take = Send(w =>
        {
            w.Write(PayItemRequest.UseItem); w.Write(gift.StorageSeq); w.Write(gift.StorageItemSeq); w.Write((byte)'C');
        });
        Check(open.SequenceEqual(new byte[] { 227, PayItemRequest.CanIOpenIngameshop, PayItemResultCode.CaniopeningameshopFail })
            && buy.SequenceEqual(new byte[] { 227, PayItemRequest.Buy, PayItemResultCode.BuyFunctionFail })
            && present[1] == PayItemRequest.PresentItem && present[2] == PayItemResultCode.PresentItemFunctionFail
            && storedAfterShop == 0,
            "deferred cash shop refuses shop-open, Buy and Present without adding storage rows");
        Check(take[1] == PayItemRequest.UseItem && take[2] == PayItemResultCode.UseItemSuccess
            && _payItemStore.Get(_accountB).Count == 0
            && _playerStore.GetInventory(_ownedB.Pid, () => []).Any(item => item.ItemId == package.ItemId),
            "GM paid-storage gifts can still be taken out while the shop is closed");
    }

    // The op78 handlers record what the partner was shown and the commit checks it.
    private void TradeManagerRefusesChangedOffer()
    {
        int sellerPid = _characterStore.Create(920640, "TradeSnapA", 1, new Custom()).Pid;
        int buyerPid = _characterStore.Create(920641, "TradeSnapB", 1, new Custom()).Pid;
        using var seller = new AuditGameSocket(sellerPid, _loggerFactory);
        using var buyer = new AuditGameSocket(buyerPid, _loggerFactory);
        foreach (var (socket, pid) in new[] { (seller, sellerPid), (buyer, buyerPid) })
        {
            ResetCharacter(pid);
            _playerStore.GetInventory(pid, () => []);
            _playerManager.SetOnline(pid, socket.Client);
            socket.Client.Player = _playerManager.Get(pid);
            _playerManager.Get(pid).MarkSpawned();
            _playerManager.Get(pid).CityMap = 0;
        }
        var offered = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 4, ItemId = 604221, Count = 3 };
        var other = new PlayerStore.GenericItem { InventoryIndex = 0, InventoryCell = 20, ItemId = 604222, Count = 1 };
        _playerStore.GetInventory(sellerPid, () => []).AddRange([offered, other]);

        byte[] Ask(byte action, int target)
        {
            var wire = new byte[6]; wire[0] = 78; wire[1] = action;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(2), target);
            return wire;
        }
        // Opens a trade, offers MAIN cell 4, confirms on both sides with `between` run after the
        // seller's confirmation, and returns the op85 action each side ends on.
        (byte Seller, byte Buyer) RunTrade(Action between)
        {
            Game.OnHandlePacket(seller.Client, Ask(TradeAction.Ask, buyerPid)); _ = buyer.Read();
            Game.OnHandlePacket(buyer.Client, Ask(TradeAction.Accept, sellerPid)); _ = seller.Read();
            Game.OnHandlePacket(seller.Client, [78, TradeAction.AddItem, 0, 4]); _ = seller.Read(); _ = buyer.Read();
            Game.OnHandlePacket(seller.Client, [78, TradeAction.AskCompletion]); _ = buyer.Read();
            between();
            Game.OnHandlePacket(buyer.Client, [78, TradeAction.AskCompletion]); _ = seller.Read();
            return (seller.Read()[1], buyer.Read()[1]);
        }

        // Swap cells 4 and 20 after the buyer saw the three-stack in cell 4.
        var swapped = RunTrade(() => { offered.InventoryCell = 20; other.InventoryCell = 4; });
        Check(swapped == (TradeAction.Fail, TradeAction.Fail)
            && _playerStore.GetInventory(buyerPid, () => []).Count == 0
            && _playerStore.GetInventory(sellerPid, () => []).Count == 2,
            "op78 trade fails when the offered MAIN cell is swapped after both sides saw it");
        offered.InventoryCell = 4; other.InventoryCell = 20;

        var unchanged = RunTrade(() => { });
        Check(unchanged == (TradeAction.Completion, TradeAction.Completion)
            && _playerStore.GetInventory(buyerPid, () => []).Single() is { ItemId: 604221, Count: 3 },
            "op78 trade commits an offered item that did not change");

        _playerManager.SetOffline(sellerPid); _playerManager.SetOffline(buyerPid);
    }

    // Mail CR, auction payouts and trades refuse instead of truncating at PlayerStore.MaxMoney.
    private void MoneyCapCallersRefuse()
    {
        const int richPid = 920650, otherPid = 920651;
        ResetCharacter(richPid); ResetCharacter(otherPid);
        _transmitStore.DeleteCharacter(richPid);
        var rich = _playerStore.GetProgress(richPid);

        var mail = _transmitStore.Add(richPid, 0, 4, "System", "CR", "", cr: 1000);
        rich.Money = PlayerStore.MaxMoney - 100;
        var refused = _transmitManager.TransmitGetCR(richPid, mail.Sequence);
        Check(refused.Answer == TransmitResultCode.Error
            && _transmitStore.Get(richPid, mail.Sequence)!.CR == 1000
            && rich.Money == PlayerStore.MaxMoney - 100,
            "a mail CR claim past the money cap is refused and the CR stays on the mail");
        rich.Money = 0;
        var claimed = _transmitManager.TransmitGetCR(richPid, mail.Sequence);
        Check(claimed.Answer == TransmitResultCode.Ok && rich.Money == 1000
            && _transmitStore.Get(richPid, mail.Sequence)!.CR == 0,
            "a mail CR claim under the money cap is credited in full");
        _transmitStore.DeleteCharacter(richPid);

        // Auction: the seller is near the cap when a buyer bids.
        _playerStore.GetInventory(otherPid, () => []);
        _playerStore.GetInventory(richPid, () => []).Add(new PlayerStore.GenericItem
            { InventoryIndex = 0, InventoryCell = 3, ItemId = 604221, Count = 1 });
        rich.Money = 1_000_000;
        Check(_auctionStore.Register(richPid, "Rich", 3, 5000, 0, out var listing) == AuctionStore.Result.Success,
            "money-cap auction fixture registers a listing");
        rich.Money = PlayerStore.MaxMoney - 10;
        var buyer = _playerStore.GetProgress(otherPid);
        buyer.Money = 10_000;
        Check(_auctionStore.Bid(otherPid, listing!.Index, out var nothing) == AuctionStore.Result.Error
            && nothing == null && buyer.Money == 10_000 && rich.Money == PlayerStore.MaxMoney - 10
            && _playerStore.GetInventory(otherPid, () => []).Count == 0
            && _auctionStore.All().Any(x => x.Index == listing.Index),
            "an auction bid whose payout would pass the seller's money cap is refused before charging the buyer");
        rich.Money = 0;
        Check(_auctionStore.Bid(otherPid, listing.Index, out var bought) == AuctionStore.Result.Success
            && bought != null && buyer.Money == 5000 && rich.Money == 5000,
            "the same auction bid succeeds once the seller has room");

        // Trade: either side's post-trade balance past the cap refuses the whole exchange.
        rich.Money = PlayerStore.MaxMoney - 10;
        buyer.Money = 1000;
        var none = new Dictionary<byte, byte>();
        Check(_playerStore.CompleteTrade(otherPid, none, 100, richPid, none, 0) == PlayerStore.TradeCommitResult.Invalid
            && rich.Money == PlayerStore.MaxMoney - 10 && buyer.Money == 1000,
            "a trade that would push a balance past the money cap is refused with Invalid");
        Check(_playerStore.CompleteTrade(otherPid, none, 10, richPid, none, 0) == PlayerStore.TradeCommitResult.Success
            && rich.Money == PlayerStore.MaxMoney && buyer.Money == 990,
            "a trade that lands exactly on the money cap still commits");
    }

    // The player-mail cap and throttle leave scripted quest mail and corrected retries alone.
    private void MailCapExemptionsAndRetries()
    {
        var questCharacter = _characterStore.Create(920660, "MailQuest920660", 1, new Custom());
        int questPid = questCharacter.Pid;
        _transmitStore.DeleteCharacter(questPid);
        for (int i = 0; i < TransmitStore.MailboxCapacity; i++)
            _transmitStore.Add(questPid, 0, 4, "System", "Reward", "");
        Check(!_transmitStore.IsMailboxFull(questPid),
            "server-issued mail does not count toward the player-mail cap");
        for (int i = 0; i < TransmitStore.MailboxCapacity; i++)
            _transmitStore.Add(questPid, 7, 1, "Player", "Filler", "");
        using (var questSocket = new AuditGameSocket(questPid, _loggerFactory))
        {
            questSocket.Client.Player = _playerManager.Get(questPid);
            byte[] QuestMail()
            {
                using var wire = new PacketWriter();
                wire.Write((byte)181); wire.Write((byte)3); wire.Write("Polly"); wire.Write(questCharacter.Name);
                wire.Write("Orders"); wire.Write("body"); wire.Write(920660);
                return questSocket.Request(wire.Buffer, 201);
            }
            int before = _transmitStore.List(questPid).Count;
            var delivered = QuestMail();
            var repeated = QuestMail();
            Check(_transmitStore.IsMailboxFull(questPid)
                && delivered[1] == TransmitResultCode.Ok && repeated[1] == TransmitResultCode.Duplicate
                && _transmitStore.List(questPid).Count == before + 1,
                "scripted quest mail reaches a full mailbox and a repeat still answers TRANSMITRET_DUPLICATE");
        }
        _transmitStore.DeleteCharacter(questPid);

        // A refused send does not start the interval; a delivered one does; the daily cap refuses.
        using var socket = new AuditGameSocket(920692, _loggerFactory);
        socket.Client.Player = _playerManager.Get(_ownedB.Pid);
        _transmitManager._lastTransmitSend.Remove(_ownedB.Pid);
        _transmitManager._dailyTransmitSends.Remove(_ownedB.Pid);
        _playerStore.GetProgress(_ownedB.Pid).Money = 100;
        byte[] SendMail(int cr)
        {
            using var mail = new PacketWriter();
            mail.Write((byte)180); mail.Write(0); mail.Write(_ownedA.Name);
            mail.Write("Retry"); mail.Write("body"); mail.Write(cr);
            mail.Write(byte.MaxValue); mail.Write(byte.MaxValue); mail.Write(byte.MaxValue);
            return socket.Request(mail.Buffer, 201);
        }
        var poor = SendMail(1_000_000);
        var retry = SendMail(0);
        var flood = SendMail(0);
        Check(poor[1] == TransmitResultCode.NotEnoughMoney && retry[1] == TransmitResultCode.Ok
            && flood[1] == TransmitResultCode.Error,
            "a refused mail send can be corrected and retried at once; a delivered send starts the interval");

        _transmitManager._lastTransmitSend.Remove(_ownedB.Pid);
        _transmitManager._dailyTransmitSends[_ownedB.Pid] =
            (TransmitManager.UtcDay(), TransmitManager.TransmitDailySendLimit);
        int beforeDaily = _transmitStore.List(_ownedA.Pid).Count;
        var overDaily = SendMail(0);
        Check(overDaily[1] == TransmitResultCode.Error && _transmitStore.List(_ownedA.Pid).Count == beforeDaily,
            "player mail past the per-sender daily limit is refused and stores nothing");
        _transmitManager._dailyTransmitSends[_ownedB.Pid] = (TransmitManager.UtcDay() - 1, TransmitManager.TransmitDailySendLimit);
        Check(SendMail(0)[1] == TransmitResultCode.Ok, "the per-sender daily mail limit resets on the next UTC day");
        _transmitManager._lastTransmitSend.Remove(_ownedB.Pid);
        _transmitManager._dailyTransmitSends.Remove(_ownedB.Pid);

        // Sequences come from a running counter and are never reused after the newest is deleted.
        var first = _transmitStore.Add(questPid, 0, 4, "System", "Seq", "");
        var second = _transmitStore.Add(questPid, 0, 4, "System", "Seq", "");
        _transmitStore.Modify(questPid, second.Sequence, 1);
        var third = _transmitStore.Add(questPid, 0, 4, "System", "Seq", "");
        Check(second.Sequence == first.Sequence + 1 && third.Sequence == second.Sequence + 1,
            "mail sequences are not reused after the newest message is deleted");
        _transmitStore.DeleteCharacter(questPid);
    }

    // op12 carries the client's int nMoney; a long starting balance is clamped, not wrapped.
    private void CreatePlayerMoneyClamp()
    {
        string? previous = Environment.GetEnvironmentVariable("HUXLEY_STARTING_MONEY");
        Environment.SetEnvironmentVariable("HUXLEY_STARTING_MONEY", "5000000000");
        try
        {
            using var socket = new AuditGameSocket(920693, _loggerFactory);
            socket.Client.Session.AccountId = 920693001;
            using var create = new PacketWriter();
            create.Write((byte)11); create.Write("Cap" + Random.Shared.Next(1_000_000, 9_999_999));
            create.Write((byte)1); new Custom().Serialize(create);
            var reply = socket.Request(create.Buffer, 12);
            var reader = new PacketReader(reply[1..]);
            bool parsed = reader.TryRead(out byte answer) && reader.TryRead(out int pid)
                && reader.TryRead(out string? _) && reader.TryRead(out byte _) && reader.TryRead(out int money)
                && answer == 0 && money == int.MaxValue
                && _playerStore.GetProgress(pid).Money == 5_000_000_000;
            Check(parsed, "op12 CreatePlayer clamps a long starting balance to the client's int money field");
        }
        finally { Environment.SetEnvironmentVariable("HUXLEY_STARTING_MONEY", previous); }
    }
}
