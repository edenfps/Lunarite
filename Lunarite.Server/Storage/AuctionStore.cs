using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Storage;

public sealed class AuctionStore
{
    private readonly ILogger _logger;
    private readonly EconomyRates _economyRates;
    private readonly EquipmentRules _equipmentRules;
    private readonly LicenseStore _licenseStore;
    private readonly PlayerStore _playerStore;

    public static readonly TimeSpan ListingLifetime = TimeSpan.FromHours(24);

    public sealed class AuctionItemInfo
    {
        public int Index { get; set; }

        public int SellerPid { get; set; }

        public string Seller { get; set; } = "";

        public int Price { get; set; }

        public byte Period { get; set; }

        public DateTime CreatedUtc { get; set; }

        public PlayerStore.GenericItem Item { get; set; } = new();
    }

    public readonly record struct ReturnedItem(PlayerStore.GenericItem Item, int ItemId);

    public enum Result : byte
    {
        Success = 0, Error = 1, ItemNotExist = 2, BindItem = 3, NeedRepair = 4,
        NotEnoughMoney = 5, UsingInventory = 6, BidNotExist = 7, InventoryFull = 8,
        AuctionNotExist = 9, OverCount = 10, TimeoutDeleted = 11, WrongMoney = 12,
        CantBidMine = 13, InventoryBlocked = 14, ItemBlocked = 15, CantRegister = 16
    }

    private static readonly object Lock = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "auctions.json");
    private static readonly string SequencePath = Path.Combine(AppContext.BaseDirectory, "auction-sequence.json");
    private readonly List<AuctionItemInfo> Listings;

    private sealed class AuctionSequence { public int LastIndex { get; set; } }

    private readonly (int Last, bool Durable) InitialSequence;
    private int _lastIndex;
    private bool _sequenceDurable;

    private readonly ItemCatalog _itemCatalog;

    private readonly AtomicJson _atomicJson;

    public AuctionStore(ILogger<AuctionStore> logger, EconomyRates economyRates,
        EquipmentRules equipmentRules, LicenseStore licenseStore, PlayerStore playerStore, ItemCatalog itemCatalog, AtomicJson atomicJson)
    {
        _logger = logger;
        _economyRates = economyRates;
        _equipmentRules = equipmentRules;
        _licenseStore = licenseStore;
        _playerStore = playerStore;
        _itemCatalog = itemCatalog;
        _atomicJson = atomicJson;
        Listings = Load();
        InitialSequence = LoadSequence();
        _lastIndex = InitialSequence.Last;
        _sequenceDurable = InitialSequence.Durable;
    }

    private (int Last, bool Durable) LoadSequence()
    {
        int activeMax = Listings.Count == 0 ? 0 : Listings.Max(x => x.Index);
        var sequence = _atomicJson.Read<AuctionSequence>(SequencePath);
        if (sequence != null && sequence.LastIndex < 0)
            throw new InvalidDataException("Auction index sequence is negative.");
        return (Math.Max(activeMax, sequence?.LastIndex ?? 0),
            sequence != null && sequence.LastIndex >= activeMax);
    }

    // An abandoned reservation only skips an index. The sequence is written before any escrow
    // change is published.
    private void PersistLastIndex(int index)
    {
        string generation = JsonSerializer.Serialize(new AuctionSequence { LastIndex = index });
        // Reserved IDs commit immediately, outside any enclosing gameplay transaction.
        _atomicJson.Publish(SequencePath, generation);
        _lastIndex = index;
        _sequenceDurable = true;
    }

    private void EnsureSequenceDurable()
    {
        if (!_sequenceDurable) PersistLastIndex(_lastIndex);
    }

    private List<AuctionItemInfo> Load()
    {
        try
        {
            return _atomicJson.Read<List<AuctionItemInfo>>(PathName) ?? [];
        }
        catch (Exception ex) { _logger.LogWarning("[Auction] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try
        {
            _atomicJson.Write(PathName,
                JsonSerializer.Serialize(Listings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { _logger.LogWarning("[Auction] save failed: {0}", ex.Message); }
    }

    public IReadOnlyList<AuctionItemInfo> All()
    {
        lock (Lock) return Listings.Select(Clone).ToList();
    }

    public IReadOnlyList<AuctionItemInfo> Active(DateTime? utcNow = null)
    {
        DateTime now = utcNow ?? DateTime.UtcNow;
        lock (Lock) return Listings.Where(x => !IsTimedOut(x, now)).Select(Clone).ToList();
    }

    public IReadOnlyList<AuctionItemInfo> TimedOut(int pid, DateTime? utcNow = null)
    {
        DateTime now = utcNow ?? DateTime.UtcNow;
        lock (Lock) return Listings.Where(x => x.SellerPid == pid && IsTimedOut(x, now)).Select(Clone).ToList();
    }

    public bool HasTimedOut(int pid, DateTime? utcNow = null)
    {
        DateTime now = utcNow ?? DateTime.UtcNow;
        lock (Lock) return Listings.Any(x => x.SellerPid == pid && IsTimedOut(x, now));
    }

    // Listed items are held here in escrow; character deletion checks this first.
    public bool HasListings(int pid)
    {
        lock (Lock) return Listings.Any(x => x.SellerPid == pid);
    }

    public IReadOnlyList<AuctionItemInfo> Search(int buyerPid, byte category, bool usable, byte grade,
        string? search, DateTime? utcNow = null)
    {
        IEnumerable<AuctionItemInfo> query = Active(utcNow);
        query = query.Where(x => _itemCatalog.Get(x.Item.ItemId)?.AuctionCategory == category);
        if (grade < 8) query = query.Where(x => _itemCatalog.Get(x.Item.ItemId)?.ItemGrade == grade);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Seller.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Item.ItemId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || (ItemLocalization.Get(x.Item.ItemId, "name")?.Contains(search,
                    StringComparison.OrdinalIgnoreCase) ?? false));
        if (usable)
            query = query.Where(x => _equipmentRules.IsUsable(buyerPid, x.Item,
                _itemCatalog.Get(x.Item.ItemId)));
        return query.ToList();
    }

    public static bool IsTimedOut(AuctionItemInfo listing, DateTime? utcNow = null) =>
        (utcNow ?? DateTime.UtcNow) - listing.CreatedUtc >= ListingLifetime;

    // EAUCTION_REMAIN_TIME from HuxleySubsystem.uc: 12-24h, 2-12h, 30m-2h,
    // deadline-30m, then over deadline.
    public static byte RemainTime(AuctionItemInfo listing, DateTime? utcNow = null)
    {
        TimeSpan remaining = listing.CreatedUtc + ListingLifetime - (utcNow ?? DateTime.UtcNow);
        if (remaining <= TimeSpan.Zero) return 4;
        if (remaining <= TimeSpan.FromMinutes(30)) return 3;
        if (remaining <= TimeSpan.FromHours(2)) return 2;
        if (remaining <= TimeSpan.FromHours(12)) return 1;
        return 0;
    }

    // HuxleyAuctionItemSellMenu first calls GetRealPrice(Data.Price, PRI, false),
    // which truncates the faction buy-rate result to int. It then truncates
    // realPrice * count * FeeRate before applying the ECTLG_Auction license
    // multiplier and truncating again. Retail FeeRate is 0.2f.
    public int RegistrationFee(int pid, PlayerStore.GenericItem item)
    {
        var data = _itemCatalog.Get(item.ItemId);
        if (data == null || data.Price <= 0 || item.Count <= 0) return 0;
int RetailInt(float value) =>
            (int)Math.Clamp((double)value, 0, int.MaxValue);
        int realPrice = RetailInt(data.Price * _economyRates.ForPid(pid).Buy);
        int baseFee = RetailInt(realPrice * (float)item.Count * 0.2f);
        int discount = Math.Clamp(_licenseStore.AuctionDiscountPercent(pid), 0, 100);
        return RetailInt(baseFee * (1.0f - discount * 0.01f));
    }

    public Result Register(int pid, string seller, byte mainCell, int price, byte period,
        out AuctionItemInfo? listing, DateTime? createdUtc = null)
    {
        listing = null;
        if (price <= 0) return Result.WrongMoney;
        // The sell menu always submits period 0; no other duration is known.
        if (period != 0) return Result.CantRegister;
        lock (Lock)
        {
            if (Listings.Count(x => x.SellerPid == pid) >= 20) return Result.OverCount;
            var source = _playerStore.GetInventory(pid, () => [])
                .FirstOrDefault(x => x.InventoryIndex == 0 && x.InventoryCell == mainCell);
            if (source == null) return Result.ItemNotExist;
            // HuxleyAuctionItemSellMenu checks the low-nibble bind state; Skill0/Skill1 are socket
            // data, not ownership flags.
            if ((source.State & 0x0F) != 0) return Result.BindItem;
            // HuxleyItemSystemHelper.IsRepairable acts as the damaged-item test. Non-repairable
            // stackables have durability 0 and stay auctionable.
            if (_itemCatalog.Get(source.ItemId)?.Repairable == true && source.Durability < 100)
                return Result.NeedRepair;
            int index = checked(_lastIndex + 1);
            PersistLastIndex(index);
            int fee = RegistrationFee(pid, source);
            if (!_playerStore.TrySpendMoney(pid, fee)) return Result.NotEnoughMoney;
            if (!_playerStore.TryRemoveMainItems(pid, [mainCell], out var removed) || removed.Count != 1)
            {
                _playerStore.AddMoney(pid, fee);
                return Result.ItemNotExist;
            }
            var item = removed[0];
            listing = new AuctionItemInfo
            {
                Index = index, SellerPid = pid, Seller = seller, Price = price,
                Period = period, CreatedUtc = createdUtc ?? DateTime.UtcNow, Item = item
            };
            Listings.Add(listing); Save();
            return Result.Success;
        }
    }

    public Result Cancel(int pid, int index, out PlayerStore.GenericItem? returned)
    {
        returned = null;
        lock (Lock)
        {
            var listing = Listings.FirstOrDefault(x => x.Index == index);
            if (listing == null || listing.SellerPid != pid) return Result.AuctionNotExist;
            EnsureSequenceDurable();
            if (!_playerStore.TryAddMainItem(pid, listing.Item, out var deposited)) return Result.InventoryFull;
            Listings.Remove(listing); Save(); returned = deposited;
            return Result.Success;
        }
    }

    public Result Bid(int buyerPid, int index, out PlayerStore.GenericItem? purchased)
        => Bid(buyerPid, index, out purchased, out _);

    public Result Bid(int buyerPid, int index, out PlayerStore.GenericItem? purchased,
        out AuctionItemInfo? sold, byte? category = null)
    {
        purchased = null;
        sold = null;
        lock (Lock)
        {
            var listing = Listings.FirstOrDefault(x => x.Index == index);
            if (listing == null) return Result.AuctionNotExist;
            // HuxleyAuctionMenu submits the category of the result it displayed; a mismatch
            // means a stale selection.
            if (category.HasValue && _itemCatalog.Get(listing.Item.ItemId)?.AuctionCategory != category.Value)
                return Result.AuctionNotExist;
            if (IsTimedOut(listing)) return Result.TimeoutDeleted;
            if (listing.SellerPid == buyerPid) return Result.CantBidMine;
            // A payout past PlayerStore.MaxMoney would be truncated; refuse before charging the buyer.
            // EAUCTION_RESULT has no max-money entry, so it answers AUCTION_ERROR.
            if (!_playerStore.CanReceiveMoney(listing.SellerPid, listing.Price)) return Result.Error;
            EnsureSequenceDurable();
            if (!_playerStore.TrySpendMoney(buyerPid, listing.Price)) return Result.NotEnoughMoney;
            if (!_playerStore.TryAddMainItem(buyerPid, listing.Item, out var deposited))
            {
                _playerStore.AddMoney(buyerPid, listing.Price);
                return Result.InventoryFull;
            }
            // CanReceiveMoney above only picks the result code; the credit itself refuses atomically,
            // so a concurrent credit to the seller cannot truncate this payout.
            if (!_playerStore.TryAddMoney(listing.SellerPid, listing.Price))
            {
                _playerStore.RemoveMainItem(buyerPid, deposited.InventoryCell);
                _playerStore.AddMoney(buyerPid, listing.Price);
                return Result.Error;
            }
            Listings.Remove(listing); Save(); purchased = deposited; sold = Clone(listing);
            return Result.Success;
        }
    }

    public List<PlayerStore.GenericItem> ReturnTimedOut(int pid, DateTime? utcNow = null)
        => ReturnTimedOutDetailed(pid, utcNow).Select(x => x.Item).ToList();

    public List<ReturnedItem> ReturnTimedOutDetailed(int pid, DateTime? utcNow = null)
    {
        DateTime now = utcNow ?? DateTime.UtcNow;
        lock (Lock)
        {
            var returned = new List<ReturnedItem>();
            var timedOut = Listings.Where(x => x.SellerPid == pid && IsTimedOut(x, now)).ToList();
            if (timedOut.Count != 0) EnsureSequenceDurable();
            foreach (var listing in timedOut)
            {
                if (!_playerStore.TryAddMainItem(pid, listing.Item, out var deposited)) break;
                returned.Add(new ReturnedItem(deposited, listing.Item.ItemId)); Listings.Remove(listing);
            }
            if (returned.Count != 0) Save();
            return returned;
        }
    }

    private static AuctionItemInfo Clone(AuctionItemInfo x) => new()
    {
        Index = x.Index, SellerPid = x.SellerPid, Seller = x.Seller, Price = x.Price,
        Period = x.Period, CreatedUtc = x.CreatedUtc,
        Item = new PlayerStore.GenericItem
        {
            InventoryIndex = x.Item.InventoryIndex, InventoryCell = x.Item.InventoryCell, ItemId = x.Item.ItemId,
            Count = x.Item.Count, State = x.Item.State, LimitTime = x.Item.LimitTime,
            TuneUpOptions = x.Item.TuneUpOptions, Durability = x.Item.Durability,
            Skill = x.Item.Skill, Skill1 = x.Item.Skill1
        }
    };
}
