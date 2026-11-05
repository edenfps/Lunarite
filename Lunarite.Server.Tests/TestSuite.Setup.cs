using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite;
using Lunarite.Network;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void CreateServices()
    {
        _services = new ServiceCollection()
            .AddServerServices()
            .BuildServiceProvider();
        _loggerFactory = _services.GetRequiredService<ILoggerFactory>();
        _battleFront = _services.GetRequiredService<BattleFront>();
        _battleZoneCatalog = _services.GetRequiredService<BattleZoneCatalog>();
        _cityTransportCatalog = _services.GetRequiredService<CityTransportCatalog>();
        _cityZoneCatalog = _services.GetRequiredService<CityZoneCatalog>();
        _craftCatalog = _services.GetRequiredService<CraftCatalog>();
        _itemCatalog = _services.GetRequiredService<ItemCatalog>();
        var itemGroupCatalog = _services.GetRequiredService<ItemGroupCatalog>();
        _licenseCatalog = _services.GetRequiredService<LicenseCatalog>();
        _personalBuffCatalog = _services.GetRequiredService<PersonalBuffCatalog>();
        _questCatalog = _services.GetRequiredService<QuestCatalog>();
        _questLoot = _services.GetRequiredService<QuestLoot>();
        _serverEvents = _services.GetRequiredService<ServerEvents>();
        _simulationMapOverrides = _services.GetRequiredService<SimulationMapOverrides>();
        _sqliteStore = _services.GetRequiredService<SqliteStore>();
        _atomicJson = _services.GetRequiredService<AtomicJson>();
        _multiStoreTransaction = _services.GetRequiredService<MultiStoreTransaction>();
        _stateBackup = _services.GetRequiredService<StateBackup>();
        _rankTable = _services.GetRequiredService<RankTable>();

        // Retail entitlement tests below assert exact durative lists and capacities; the server policies are checked on their own.
        ServerPolicy.OpenAllWeaponSlots = false;
        ServerPolicy.MaxExpansionByDefault = false;
        // A host on the server's PC (127.0.0.2) is advertised to a remote guest as the server address
        // that guest reached (203.0.113.10); local guests and remote hosts keep their own address.
        Check(P2PManager.ReachableHostIp(0x0200007F, viewerIsLocal: false, 0x0A7100CB) == 0x0A7100CB
            && P2PManager.ReachableHostIp(0x0200007F, viewerIsLocal: true, 0x0100007F) == 0x0200007F
            && P2PManager.ReachableHostIp(0x146433C6, viewerIsLocal: false, 0x0A7100CB) == 0x146433C6,
            "loopback hosts are advertised to remote guests at the server's guest-facing address");
        // Test guests never run a real UDP hole punch, so op52 follows op128 without waiting for op118.
        Environment.SetEnvironmentVariable("QUEST_HOLEPUNCH_WAIT_MS", "0");
        Environment.SetEnvironmentVariable("QUEST_HOLEPUNCH_ATTEMPTS", "1");
        Check(itemGroupCatalog.All.Count == 43
            && ItemGroupCatalog.CityVendorGroupCount == 26
            && itemGroupCatalog.All["W_CB"].Contains(100101)
            && itemGroupCatalog.All["Ticket_T"].SequenceEqual(new[] { 609201, 609301, 609302 })
            && itemGroupCatalog.IsVendorItem(100101)
            && itemGroupCatalog.IsVendorItem(609201)
            && itemGroupCatalog.All["W_T"].Contains(100103)
            && !itemGroupCatalog.IsVendorItem(100103)
            && !itemGroupCatalog.IsVendorItem(604221)
            && itemGroupCatalog.IsVendorItem(608002) && !itemGroupCatalog.IsVendorItem(608001),
            "city-script vendor groups retain exact item membership and exclude unreferenced cooked groups");
        // Exact ItemGroups intersection with OpenTradeMenu GroupName values from all 28 cooked
        // city *Script*.war packages. Two further names have no ItemGroup and remain excluded.
        string[] cityVendorGroups = ["A_AS_H", "A_AS_L", "A_AS_M", "A_BA_H", "A_BA_L",
            "A_BA_M", "A_CB_H", "A_CB_L", "A_CB_M", "A_S_H", "A_S_L", "A_S_M",
            "BI_AS", "BI_BA", "BI_CB", "RCP_BA", "SK_Me_AS", "SK_Me_BA", "SK_Me_CB",
            "Ticket_T", "Trans_Ticket", "Vehicle_T", "W_AS", "W_BA", "W_CB", "W_S"];
        Check(ItemGroupCatalog.CityVendorGroupNames.SetEquals(cityVendorGroups),
            "purchase allowlist matches every cooked city OpenTradeMenu item group");
        var unresolvedCityVendorIds = cityVendorGroups.SelectMany(name => itemGroupCatalog.All[name])
            .Distinct().Where(itemId => _itemCatalog.Get(itemId) == null).Order().ToArray();
        Check(unresolvedCityVendorIds.Length == 0,
            $"city vendor groups resolve to item definitions (missing: {string.Join(',', unresolvedCityVendorIds.Take(20))})");
    }

    private void ResolveServices()
    {
        Directory.Delete(_stagedRestore, recursive: true);

        // Stores load their files when first resolved, after the data reset above.
        _services.ConfigurePacketHandlers();
        _accountSessionStore = _services.GetRequiredService<AccountSessionStore>();
        _accountStore = _services.GetRequiredService<AccountStore>();
        _auctionStore = _services.GetRequiredService<AuctionStore>();
        _battleResultStore = _services.GetRequiredService<BattleResultStore>();
        _blockStore = _services.GetRequiredService<BlockStore>();
        _characterDataLifecycle = _services.GetRequiredService<CharacterDataLifecycle>();
        _characterStore = _services.GetRequiredService<CharacterStore>();
        _clanStore = _services.GetRequiredService<ClanStore>();
        _craftStore = _services.GetRequiredService<CraftStore>();
        _durativeItemStore = _services.GetRequiredService<DurativeItemStore>();
        _economyRates = _services.GetRequiredService<EconomyRates>();
        _equipmentRules = _services.GetRequiredService<EquipmentRules>();
        _friendStore = _services.GetRequiredService<FriendStore>();
        _licenseStore = _services.GetRequiredService<LicenseStore>();
        _payItemStore = _services.GetRequiredService<PayItemStore>();
        _personalBuffStore = _services.GetRequiredService<PersonalBuffStore>();
        _playerManager = _services.GetRequiredService<PlayerManager>();
        _questRoomMembers = _services.GetRequiredService<QuestRoomMembers>();
        _cashShopCatalog = _services.GetRequiredService<CashShopCatalog>();
        _playerStore = _services.GetRequiredService<PlayerStore>();
        _questConditions = _services.GetRequiredService<QuestConditions>();
        _questEntryReservationStore = _services.GetRequiredService<QuestEntryReservationStore>();
        _questRewards = _services.GetRequiredService<QuestRewards>();
        _questStore = _services.GetRequiredService<QuestStore>();
        _transmitStore = _services.GetRequiredService<TransmitStore>();
        _battleManager = _services.GetRequiredService<BattleManager>();
        _characterManager = _services.GetRequiredService<CharacterManager>();
        _cityManager = _services.GetRequiredService<CityManager>();
        _clanManager = _services.GetRequiredService<ClanManager>();
        _gmCommands = _services.GetRequiredService<GmCommands>();
        _itemManager = _services.GetRequiredService<ItemManager>();
        _questInstanceManager = _services.GetRequiredService<QuestInstanceManager>();
        _questManager = _services.GetRequiredService<QuestManager>();
        _squadManager = _services.GetRequiredService<SquadManager>();
        _transmitManager = _services.GetRequiredService<TransmitManager>();
        _battleServerManager = _services.GetRequiredService<BattleServerManager>();
        _questGateway = _services.GetRequiredService<QuestGateway>();
        _p2pManager = _services.GetRequiredService<P2PManager>();
        _simulationManager = _services.GetRequiredService<SimulationManager>();
    }
}
