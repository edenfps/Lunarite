using System.Buffers.Binary;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Network.Ue3;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

// Shared fixtures and helpers for the feature checks in TestSuite.<Feature>.cs, which run in
// the order below because later checks build on state earlier ones leave behind.
internal sealed partial class TestSuite : IAsyncDisposable
{
    private const int Sender = 910001;
    private const int Recipient = 910002;
    private const int ExpandPid = 920096;
    private const int LevelPid = 920060;
    private const int EntryItemPid = 920061;
    private const int SkillPid = 920007;
    private const int OfflineGuestPid = 920008;
    private const int OfflineHostPid = 920007;
    private const int OfflineGuestClientId = 920008;
    private const int VendorGatePid = 920106;
    private const int AcquiredCityQuest = 821900;
    private const int OptionalQuestIndex = 822807;
    private const int TakeoffPid = 920004;
    private const int QtBuyerPid = 920105;
    private const int DurabilityPid = 920002;
    private const int QuestRecordPid = 920044;
    private const int EntryRefundPid = 920097;
    private const int QuestPid = 920001;
    private const int QuestIndex = 820102;
    private const int DirectFieldTelemetryPid = 920301;
    private const int FieldEventPid = 920094;
    private const int DeletedPid = 920090;
    private const int SurvivingPid = 920091;

    private ServiceProvider _services = null!;
    private ILoggerFactory _loggerFactory = null!;
    private BattleFront _battleFront = null!;
    private BattleZoneCatalog _battleZoneCatalog = null!;
    private CityTransportCatalog _cityTransportCatalog = null!;
    private CityZoneCatalog _cityZoneCatalog = null!;
    private CraftCatalog _craftCatalog = null!;
    private ItemCatalog _itemCatalog = null!;
    private LicenseCatalog _licenseCatalog = null!;
    private PersonalBuffCatalog _personalBuffCatalog = null!;
    private QuestCatalog _questCatalog = null!;
    private QuestLoot _questLoot = null!;
    private ServerEvents _serverEvents = null!;
    private SimulationMapOverrides _simulationMapOverrides = null!;
    private SqliteStore _sqliteStore = null!;
    private AtomicJson _atomicJson = null!;
    private MultiStoreTransaction _multiStoreTransaction = null!;
    private StateBackup _stateBackup = null!;
    private RankTable _rankTable = null!;
    private string _stagedRestore = null!;
    private AccountSessionStore _accountSessionStore = null!;
    private AccountStore _accountStore = null!;
    private AuctionStore _auctionStore = null!;
    private BattleResultStore _battleResultStore = null!;
    private BlockStore _blockStore = null!;
    private CharacterDataLifecycle _characterDataLifecycle = null!;
    private CharacterStore _characterStore = null!;
    private ClanStore _clanStore = null!;
    private CraftStore _craftStore = null!;
    private DurativeItemStore _durativeItemStore = null!;
    private EconomyRates _economyRates = null!;
    private EquipmentRules _equipmentRules = null!;
    private FriendStore _friendStore = null!;
    private LicenseStore _licenseStore = null!;
    private PayItemStore _payItemStore = null!;
    private PersonalBuffStore _personalBuffStore = null!;
    private PlayerManager _playerManager = null!;
    private QuestRoomMembers _questRoomMembers = null!;
    private CashShopCatalog _cashShopCatalog = null!;
    private PlayerStore _playerStore = null!;
    private QuestConditions _questConditions = null!;
    private QuestEntryReservationStore _questEntryReservationStore = null!;
    private QuestRewards _questRewards = null!;
    private QuestStore _questStore = null!;
    private TransmitStore _transmitStore = null!;
    private BattleManager _battleManager = null!;
    private CharacterManager _characterManager = null!;
    private CityManager _cityManager = null!;
    private ClanManager _clanManager = null!;
    private GmCommands _gmCommands = null!;
    private ItemManager _itemManager = null!;
    private QuestInstanceManager _questInstanceManager = null!;
    private QuestManager _questManager = null!;
    private SquadManager _squadManager = null!;
    private TransmitManager _transmitManager = null!;
    private BattleServerManager _battleServerManager = null!;
    private QuestGateway _questGateway = null!;
    private P2PManager _p2pManager = null!;
    private SimulationManager _simulationManager = null!;
    private int _accountA;
    private int _accountB;
    private CharacterStore.Character _ownedA = null!;
    private CharacterStore.Character _ownedB = null!;
    private int _accountC;
    private List<PlayerStore.GenericItem> _sent = null!;
    private IReadOnlyList<ItemCatalog.Item> _payItems = null!;
    private PlayerStore.PlayerProgress _classB = null!;
    private CharacterStore.Character _recommendedLevelCharacter = null!;
    private int _equipPid;
    private ItemCatalog.Item _equipFlinger = null!;
    private PlayerStore.GenericItem _equipSource = null!;
    private OwnerPingTime _ownerPings = null!;
    private EconomyRates.Snapshot _testRates;
    private byte[] _craftListWire = null!;
    private byte[] _guestTravelKey = null!;
    private PacketWriter _natReportWire = null!;
    private byte[][] _guestTravelSeparateJoin = null!;
    private System.Net.Sockets.TcpClient _questSocketPeer = null!;
    private System.Net.Sockets.TcpClient _questSocketAccepted = null!;
    private Client _questSocketClient = null!;
    private System.Net.Sockets.NetworkStream _questPeerStream = null!;
    private byte[,] _decryptTable = null!;
    private int _detachedReportQuest;
    private int _detachedGuestClientId;
    private byte[] _detachedGuestResult = null!;
    private byte[] _fieldEnemyDown = null!;
    private byte[] _directFieldRally = null!;
    private long _detachedGuestBpBefore;
    private long _detachedGuestQtBefore;
    private long _offlineHostBpAfterSettlement;
    private int _cityQuestWirePid;
    private long _cityBpBefore;
    private int _optionalQuestPid;
    private PacketWriter _questBackupWire = null!;
    private byte[] _questCreateAppearance = null!;
    private ItemCatalog.Item _vendorData = null!;
    private long _afterBuy;
    private long _pcRoomMoney;
    private int _corruptDurabilityPid;
    private byte[] _addPlayerBytes = null!;
    private byte[] _battleBlock = null!;
    private byte[] _weaponBlock = null!;
    private string _patchRoot = null!;
    private int _gmStatusUserId;
    private int _directFieldQuest;
    private QuestStore.Progress _questProgress = null!;
    private int _deliveredMailSequence;

    public async Task RunAsync()
    {
        CreateServices();
        ListenersAndAdvertisedEndpoints();
        StorePersistence();
        ResolveServices();
        AccountLoginAndRegistration();
        CharacterCreationRules();
        StarterArmorAndSimulationStart();
        CharacterRosterLimit();
        AttachmentRemoval();
        MailClaims();
        EntitlementCallbacks();
        GatewayDecoderAndItemCatalog();
        await CashShop1Async();
        InventoryExpansionAndTimers();
        StarterInventoryAndDurations();
        TuneCatalogAndBpLevels();
        LevelProgressionEvents();
        RewardGraphsAndEntryRules();
        LootTables();
        FirstMissionVariants();
        EntryItemConsumption();
        SquadEntryAndWeaponLicenses();
        WeaponSlotEntitlement();
        BindRankAndArmorEquip();
        SimulationRoomPackets();
        EconomyRatesAndPings();
        CityEventPackets();
        VehiclesAndRecipes();
        CraftingAndSockets();
        SkillEquip();
        NatPackets();
        NatReportsAndCoopPermissions();
        OfflineSquadSetup();
        VendorBuyAndMailRouting();
        UnknownEnemyDrop();
        DirectFieldSettlement();
        ScriptedPickups();
        CityKismetQuests();
        EncryptedVendorBankTram();
        FullStackSaleClearsCell();
        StarterSkillComesOff();
        HelmetSkillFitsHelmet();
        EverySocketTakesItsSkills();
        OptionalRewardQuest();
        QuestHostWorldUrl();
        ObjectiveWireFormat();
        BattleResultParsing();
        BattleBackendPackets();
        PersonalBuffsAndCashShop();
        ExpirySweepStaysQuiet();
        RallyWireFormat();
        BatchInsertPackets();
        QuestResultAndPostData();
        MissionPostDataEntitlements();
        TcpFraming();
        ItemAndTravelAnswers();
        VendorExchangeAndQuestItems();
        RepairAndSquadDirectory();
        SquadLfsAndChat();
        SquadOfflineMembersPruned();
        QuestMetadataAndCityTravel();
        PeerArmorAndQuestAcquisition();
        DeletionAndNativeIntegration();
        AuctionPacketLayouts();
        FriendsAndBlocks();
        ClanMembership();
        AuctionEscrowAndFees();
        BattleEntryPackets();
        PlayerTrade();
        BattleRecordPackets();
        MissionRunRecords();
        PatchManifestAndCrc();
        PatchBaseClient();
        PlayerLanguageChoice();
        BattleRecordResetAndInspection();
        GmAccountStatus();
        AccountBansAndListenEndpoint();
        BattlefieldFrontAndRewards();
        EntryReservationRecovery();
        EntryKeysAndCityServices();
        JournalSettlement();
        BattleResultSettlement();
        FieldObjectiveProgress();
        ObjectiveReportsAndCompletion();
        FriendCleanupSetup();
        CharacterDeletion();
        ServerLogAndGmChat();
        TuneUpCostDefaults();
        SameInventoryMergeRules();
        PartialPickupInsert();
        CoopHostCrashWhileLoading();
        CoopGuestCrashInMission();
        CoopPcRoomAndBackupLevelBp();
        BattleBackendTrust();
        BattlefieldRewardDelivery();
        BattlefieldEntryAndReservations();
        SkirmishRoomsAndResults();
        ProgressionAuditFixes();
        ChatFailuresReportToSender();
        CharacterMenuReleasesCharacter();
        PatchPublicAddressAndIdleTimeout();
        StaleTransactionSnapshotSkipped();
        ClanAppointmentGradeLimits();
        InventoryCellBoundsAndArrange();
        SkillSocketIntegrity();
        ResetToolTotalsAndRelicRepair();
        MoneyCap();
        TradeOfferSnapshotBinding();
        MailSendLimits();
        ClanInviteRevalidation();
        PaidShopDeferred();
        QuestRunReportLimits();
        PendingQuestRoomRefunds();
        KillReportsSaveDebounced();
        SimulationRewardLimits();
        MailedBattlefieldRewardsStayBound();
        GameHandoffBinding();
        SelectionGateAndPendingDeletion();
        AgentLoginAbuseLimits();
        PatchPerAddressLimit();
        SkillSocketRecovery();
        TradeManagerRefusesChangedOffer();
        MoneyCapCallersRefuse();
        MailCapExemptionsAndRetries();
        CreatePlayerMoneyClamp();
        QuestSettlementAndCensusLimits();
        QuestStoredRunTotalClamped();
        QuestHourlyBudgetsSurviveRooms();
        QuestStoreFlushesQueuedKills();
        SimulationGoalsAndPlayTimeRewards();
        TicketProbeIsBounded();
        LoginLimitsPerNameAndAddress();
        RegistrationLimits();
        GmLoginBootstrap();
        CharacterNamesFollowClientEditBox();
        QuestRunAllowancesScaleWithQuestLevel();
        QuestCompletionBooksStoredRunTotal();
        SimulationBPFollowsRetailPlayTime();
        ScriptedMailBounds();
        AtomicMoneyCredit();
        HandoffConnectBudget();
        SettingsFileParsing();
    }

    public async ValueTask DisposeAsync()
    {
        _questBackupWire?.Dispose();
        _questSocketClient?.Dispose();
        _questSocketAccepted?.Dispose();
        _questSocketPeer?.Dispose();
        _natReportWire?.Dispose();
        await Task.CompletedTask;
    }

    private long SplitRows(string table)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_sqliteStore.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return (long)command.ExecuteScalar()!;
    }

    private string? SplitKeys(string table)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_sqliteStore.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT group_concat(k, ',') FROM (SELECT k FROM \"{table}\" ORDER BY ord);";
        return command.ExecuteScalar() as string;
    }

    private byte[] ReadQuestSocketPacket()
    {
        Span<byte> header = stackalloc byte[2];
        _questPeerStream.ReadExactly(header);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(header) - 2;
        var payload = new byte[length];
        _questPeerStream.ReadExactly(payload);
        for (int i = 0; i < payload.Length; i++) payload[i] = _decryptTable[0, payload[i]];
        return payload;
    }

    // A live reward recipient first receives its Parameter/WorldEvent updates.
    private byte[] ReadQuestProgressAnswerPacket()
    {
        byte opcode = new QuestProgressAnswer { Opcode = 56 }.Serialize()[0];
        byte[] packet;
        do packet = ReadQuestSocketPacket(); while (packet[0] != opcode);
        return packet;
    }

    private void SendListenBeltUse(int wirePid)
    {
        using var request = new PacketWriter();
        request.Write((byte)116); request.Write(wirePid);
        request.Write((byte)1); request.Write((byte)0); request.Write(1);
        Game.OnHandlePacket(_questSocketClient, request.Buffer);
    }

    private byte[] ReadCityItemRemoval()
    {
        for (int i = 0; i < 30; i++)
        {
            byte[] response = ReadQuestSocketPacket();
            if (response[0] == 81) return response;
        }
        throw new InvalidOperationException("No city-script inventory removal was sent.");
    }

    private string? SearchDirectory(byte kind, string? query) =>
        CityManager.DirectoryKeyword(_questSocketClient, kind, query);

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    private void ResetCharacter(int pid)
    {
        _playerStore.ResetCharacter(pid);
        _licenseStore.Reset(pid);
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return true; }
        return false;
    }

    private static uint ReadGatewayField(string hex, int bits)
    {
        var reader = new BitReader(Convert.FromHexString(hex), bits);
        return reader.ReadIntValueAware(135);
    }
}
