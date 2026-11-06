using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed partial class ItemManager
{
    private readonly ILogger _logger;
    private readonly CityManager _cityManager;

    private readonly AccountStore _accountStore;
    private readonly BattleResultStore _battleResultStore;
    private readonly CharacterStore _characterStore;
    private readonly CraftStore _craftStore;
    private readonly DurativeItemStore _durativeItemStore;
    private readonly LicenseStore _licenseStore;
    private readonly PayItemStore _payItemStore;
    private readonly PersonalBuffStore _personalBuffStore;
    private readonly PlayerManager _playerManager;
    private readonly PlayerStore _playerStore;
    private readonly TransmitStore _transmitStore;

    private int _nextUndoSerial;

    private readonly CraftCatalog _craftCatalog;
    private readonly ItemCatalog _itemCatalog;
    private readonly LicenseCatalog _licenseCatalog;

    // Retail tutorial armor and its one socket skill. Each part adds 40 EnergyCapacity. Extra
    // manual skills add no energy; they become earlier selectable skills and displace Sprint
    // as the current skill.
    internal static readonly (int id, byte cell, int skill)[] StartingArmor =
    [
        (200199, 1, 0),      // HiBody
        (200299, 2, 0),      // LoBody
        (200399, 3, 0),      // Gloves
        (200499, 4, 303400)  // Boots + Sprint
    ];

    // Cooked HuxleyInitialItemTable group "Tutorial" (EntryIndex 500), in table order: weapons
    // 100499 Machine Gun, 100299 Flinger, 100899 Regulator; armor 200199/200299/200399/200499 with
    // the Inferior Acceleration (Sprint) socket skill 303400 on the boots.
    internal static readonly int[] TutorialWeapons = [100499, 100299, 100899];

    private readonly MultiStoreTransaction _multiStoreTransaction;

    private readonly QuestStore _questStore;

    private readonly CashShopCatalog _cashShopCatalog;

    public ItemManager(ILogger<ItemManager> logger, AccountStore accountStore,
        BattleResultStore battleResultStore, CharacterStore characterStore, CraftStore craftStore,
        DurativeItemStore durativeItemStore, LicenseStore licenseStore, PayItemStore payItemStore,
        PersonalBuffStore personalBuffStore, PlayerManager playerManager, PlayerStore playerStore,
        TransmitStore transmitStore, CityManager cityManager, CraftCatalog craftCatalog, ItemCatalog itemCatalog, LicenseCatalog licenseCatalog, MultiStoreTransaction multiStoreTransaction, QuestStore questStore, CashShopCatalog cashShopCatalog)
    {
        _cashShopCatalog = cashShopCatalog;
        _questStore = questStore;
        _logger = logger;
        _accountStore = accountStore;
        _battleResultStore = battleResultStore;
        _characterStore = characterStore;
        _craftStore = craftStore;
        _durativeItemStore = durativeItemStore;
        _licenseStore = licenseStore;
        _payItemStore = payItemStore;
        _personalBuffStore = personalBuffStore;
        _playerManager = playerManager;
        _playerStore = playerStore;
        _transmitStore = transmitStore;
        _cityManager = cityManager;
        _craftCatalog = craftCatalog;
        _itemCatalog = itemCatalog;
        _licenseCatalog = licenseCatalog;
        _multiStoreTransaction = multiStoreTransaction;
    }

    public static bool IsCityScriptItemReductionAuthorized(int selectedPid,
        int cityZone, int activeInstance, int itemIndex, int quantity)
    {
        // The six cooked Nostalonia/Eska/training-center Kismet maps containing
        // HuxleySeqAct_MoveItem link only DELETE for these two ticket/certificate IDs.
        bool scriptedZone = cityZone is 0 or 100
            || cityZone is >= 9 and <= 16
            || cityZone is >= 110 and <= 116;
        return selectedPid > 0 && scriptedZone && activeInstance <= 0 && quantity >= 0
            && (itemIndex is 613994 or 613995);
    }

    // op82 InsertItem for every slot. This fills m_SelectPlayer.m_Item, which travels into quest
    // instances via CMMOVerify::AddDatas -> arData -> AddItemByteStream.
    private void SendInventory(Client client, int pid)
    {
        var inventory = _playerStore.GetInventory(pid, () => BuildInitialInventory(pid));
        foreach (var it in inventory)
            client.SendPacket(new InsertItem
            {
                ItemID = it.ItemId,
                Count = Math.Max(1, it.Count),
                InventoryIndex = it.InventoryIndex,
                Cell = it.InventoryCell,
                State = it.State,
                LimitTime = it.LimitTime,
                TuneUpOptions = it.TuneUpOptions,
                Durability = it.Durability,
                Skill0 = it.Skill,
                Skill1 = it.Skill1
            });
        _logger.LogInformation("[Store] sent PID {0} inventory ({1} slot(s)).", pid, inventory.Count);
    }

    internal byte NextUndoSerial()
    {
        while (true)
        {
            byte serial = unchecked((byte)Interlocked.Increment(ref _nextUndoSerial));
            if (serial != 0) return serial;
        }
    }

    internal static PlayerStore.GenericItem CloneInventoryItem(PlayerStore.GenericItem item) => new()
    {
        InventoryIndex = item.InventoryIndex, InventoryCell = item.InventoryCell, ItemId = item.ItemId, Count = item.Count,
        State = item.State, LimitTime = item.LimitTime, TuneUpOptions = item.TuneUpOptions,
        Durability = item.Durability, Skill = item.Skill, Skill1 = item.Skill1
    };

    private static InsertItem ToInsertItem(PlayerStore.GenericItem item) => new()
    {
        ItemID = item.ItemId,
        Count = Math.Max(1, item.Count),
        InventoryIndex = item.InventoryIndex,
        Cell = item.InventoryCell,
        State = item.State,
        LimitTime = item.LimitTime,
        TuneUpOptions = item.TuneUpOptions,
        Durability = item.Durability,
        Skill0 = item.Skill,
        Skill1 = item.Skill1
    };

    // Per-cell diff as op217 replace, remove, or insert packets. Also used by tests.
    public static IReadOnlyList<IPacketSerialize> BuildInventorySlotChanges(
        IEnumerable<PlayerStore.GenericItem> beforeItems,
        IEnumerable<PlayerStore.GenericItem> afterItems, byte inventory)
    {
        var before = beforeItems.Where(item => item.InventoryIndex == inventory).ToDictionary(item => item.InventoryCell);
        var after = afterItems.Where(item => item.InventoryIndex == inventory).ToDictionary(item => item.InventoryCell);
        List<IPacketSerialize> packets = [];
        foreach (byte cell in before.Keys.Union(after.Keys).Order())
        {
            before.TryGetValue(cell, out var oldItem);
            after.TryGetValue(cell, out var newItem);
            if (oldItem != null && newItem != null)
            {
                if (SerializeGenericItem(oldItem).SequenceEqual(SerializeGenericItem(newItem))) continue;
                packets.Add(new ItemReplacement
                {
                    RemovedInventory = inventory,
                    RemovedCell = cell,
                    Item = ToInsertItem(newItem)
                });
            }
            else if (oldItem != null)
                packets.Add(new RemoveItem { InventoryIndex = inventory, Cell = cell });
            else if (newItem != null)
                packets.Add(ToInsertItem(newItem));
        }
        return packets;
    }

    // Items granted outside a request (battle-field rewards) reach the character's live MMO link.
    public static void PublishLiveInventoryInserts(int pid, IEnumerable<PlayerStore.GenericItem> inserted)
    {
        var client = QuestManager.QuestRewardLiveRecipient(pid);
        if (client == null) return;
        foreach (var item in inserted) SendGrantedItem(client, item);
    }

    internal static void SendInventoryItem(Client client, PlayerStore.GenericItem item) =>
        client.SendPacket(ToInsertItem(item));

    // Loot and rewards can land on an existing stack: the client refuses op82 for an occupied cell and
    // keeps its old count, so the absolute op83 count follows (a no-op for a new cell).
    internal static void SendGrantedItem(Client client, PlayerStore.GenericItem item)
    {
        SendInventoryItem(client, item);
        if (item.Count > 1)
            client.SendPacket(new UpdateItemNum { Inventory = item.InventoryIndex, Cell = item.InventoryCell, Count = item.Count });
    }

    // Server-clock sweep for online players (offline expiry happens at selection). The cell is
    // removed first, then the same op219 warning as login-time expiry.
    public void SweepExpiredItems()
    {
        int unixSeconds = checked((int)Math.Min(int.MaxValue,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        foreach (var (pid, client) in _playerManager.OnlineSnapshot())
        {
            List<PlayerStore.ExpiredItem> expiredItems = [];
            List<TransmitStore.ExpiredAttachment> expiredAttachments = [];
            _multiStoreTransaction.Run(() =>
            {
                expiredItems = _playerStore.PruneExpiredInventory(pid);
                expiredAttachments = _transmitStore.PruneExpiredAttachments(pid);
                SynchronizeQuestPickupObjectives(pid,
                    expiredItems.Where(item => item.Inventory == InventoryType.Main).Select(item => item.ItemId));
            });
            foreach (var expired in expiredItems)
            {
                client.SendPacket(new RemoveItem { InventoryIndex = expired.Inventory, Cell = expired.Cell });
                // op84 only updates the owner. Nearby pawns get the op68/op121 pair a manual
                // armor takeoff sends.
                if (Game.PlayerFor(pid).IsSpawned)
                    foreach (var packet in CityManager.BuildExpiredArmorPresence(pid, expired))
                        _cityManager.BroadcastPresence(client, packet);
                if (expired.Inventory == InventoryType.Vehicle && expired.Cell == 0
                    && Game.PlayerFor(pid).TryClearSummonedVehicle(out _))
                    _cityManager.BroadcastPresence(client, VehicleManager.BuildExpiredVehiclePresence(pid));
            }
            var expirationNotices = expiredItems
                .Select(x => (x.ItemId, x.TuneUpOptions, x.Type))
                .Concat(expiredAttachments.Select(x => (x.ItemId, x.TuneUpOptions, x.Type)))
                .ToArray();
            for (int i = 0; i < expirationNotices.Length; i++)
            {
                var expired = expirationNotices[i];
                client.SendPacket(new NotifyExpirationItem
                {
                    ItemId = expired.ItemId,
                    TuneUpOptions = expired.TuneUpOptions,
                    Type = expired.Type,
                    AfterSelectPlayer = i == expirationNotices.Length - 1
                });
            }

            // Op230 removes elapsed DurativeItems and raises the expiration notice; op232 removes
            // personal buffs and calls OnDeletedPersonalBuff.
            var expiredDurativeItems = _durativeItemStore.PruneExpired(pid, unixSeconds);
            foreach (var licenseResult in CharacterManager.BuildExpiredTemporaryLicenseResults(
                         expiredDurativeItems, _licenseStore.Effective(pid, unixSeconds)))
                client.SendPacket(licenseResult);
            var expiredBuffs = _personalBuffStore.PruneExpired(pid, unixSeconds);
            // Only when something lapsed: the client applies op230/op232 to its loaded world, and
            // one arriving while it loads a map (a tutorial or quest start) crashes it.
            if (expiredDurativeItems.Count == 0 && expiredBuffs.Count == 0) continue;
            client.SendPacket(new ServerTime
            {
                TickMilliseconds = Environment.TickCount & int.MaxValue,
                UnixSeconds = unixSeconds
            });
            client.SendPacket(new UpdateListOfDurativeItem { UnixSeconds = unixSeconds });
            client.SendPacket(new UpdateListOfPersonalBuffs { UnixSeconds = unixSeconds });
        }
    }

    internal static byte[] SerializeGenericItem(PlayerStore.GenericItem item)
    {
        using var writer = new PacketWriter();
        writer.Write(item.ItemId);
        writer.Write(item.Count);
        writer.Write(item.InventoryIndex);
        writer.Write(item.InventoryCell);
        writer.Write(item.State);
        writer.Write((byte)0); // alignment
        writer.Write(item.LimitTime);
        writer.Write(item.TuneUpOptions);
        writer.Write(item.Skill);
        writer.Write(item.Skill1);
        writer.Write(item.Durability);
        return writer.Buffer;
    }

    internal void SynchronizeQuestPickupObjective(int pid, int itemIndex)
    {
        int count = _playerStore.GetInventory(pid, () => BuildInitialInventory(pid))
            .Where(item => item.InventoryIndex == InventoryType.Main && item.ItemId == itemIndex && item.Count > 0)
            .Sum(item => item.Count);
        _questStore.SynchronizePickItemObjectives(pid, itemIndex, count);
    }

    internal void SynchronizeQuestPickupObjectives(int pid, IEnumerable<int> itemIndices)
    {
        foreach (int itemIndex in itemIndices.Where(itemIndex => itemIndex > 0).Distinct())
            SynchronizeQuestPickupObjective(pid, itemIndex);
    }

    internal void InitializeQuestInventoryObjectives(int pid, int questIndex)
    {
        var counts = _playerStore.GetInventory(pid, () => BuildInitialInventory(pid))
            .Where(item => item.InventoryIndex == InventoryType.Main && item.ItemId > 0 && item.Count > 0)
            .GroupBy(item => item.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count));
        _questStore.InitializePickItemObjectives(pid, questIndex,
            itemIndex => counts.GetValueOrDefault(itemIndex));
    }
}
