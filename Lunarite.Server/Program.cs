using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite;

static class Program
{
    static readonly List<Server> _servers = [];
    static Timer? _itemExpiryTimer;
    static Timer? _squadSweepTimer;
    static Timer? _statusFileTimer;
    static Timer? _playTimeTimer;
    static Timer? _noticeFileTimer;
    static Timer? _stateBackupTimer;
    static CashShopWebServer? _cashShopWebServer;
    static PatchServer? _patchServer;
    static ServerPingService? _serverPingService;
    static P2PNatTestService? _p2pNatTestService;

    static void Main(string[] args)
    {
        // Settings first: services read them as they are constructed.
        string? settingsPath = SettingsFile.Find();
        int settingsApplied = settingsPath != null ? SettingsFile.Apply(settingsPath) : 0;

        // Services resolve on first use: the stores load their files only after the recovery
        // and migration below.
        var serviceProvider = new ServiceCollection()
            .AddServerServices()
            .BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(nameof(Program));
        var sqliteStore = serviceProvider.GetRequiredService<SqliteStore>();
        var multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        var stateBackup = serviceProvider.GetRequiredService<StateBackup>();

        if (args.Length != 0)
        {
            if (args.Length == 3 && args[0] == "--stage-restore")
            {
                bool staged = stateBackup.TryStageRestore(args[1], args[2]);
                Console.WriteLine(staged
                    ? $"Verified recovery copy staged at {System.IO.Path.GetFullPath(args[2])}."
                    : "Recovery staging refused: verify the snapshot and choose a new, disjoint directory.");
                Environment.ExitCode = staged ? 0 : 1;
                return;
            }
            if (args.Length == 1 && args[0] == "--migrate")
            {
                multiStoreTransaction.Recover();
                int count = sqliteStore.ImportLegacyFiles();
                sqliteStore.SplitDocuments();
                Console.WriteLine($"Imported {count} JSON store(s) into {sqliteStore.DatabasePath}. Stores: "
                    + string.Join(", ", sqliteStore.Names()));
                return;
            }
            Console.Error.WriteLine("Usage: Lunarite.Server --stage-restore <snapshot-directory> <new-directory> | --migrate");
            Environment.ExitCode = 2;
            return;
        }

        Span<byte> test = [0x04, 0x00, 0xef, 0x10];

        var test2 = ComputeChecksum(test);

        Console.Title = "Huxley Server Emulator";

        AppDomain.CurrentDomain.UnhandledException += UnhandledException;

        string? logDirectory = Environment.GetEnvironmentVariable("HUXLEY_LOG_DIR");
        if (!string.IsNullOrWhiteSpace(logDirectory))
            Console.WriteLine($"Server log: {Log.EnableFile(logDirectory)}");

        logger.LogInformation(settingsPath != null
            ? $"Settings: {settingsPath} ({settingsApplied} applied; environment variables take precedence)."
            : $"Settings: no {SettingsFile.FileName} found; using environment variables and defaults.");
        logger.LogInformation("Starting servers...");

        // Reject a malformed public endpoint before accepting any client. Agent packets carry
        // only four address bytes, so DNS names and IPv6 cannot be advertised on this wire.
        _ = Lunarite.Handlers.Agent.ResolveGameAddress(
            Environment.GetEnvironmentVariable("HUXLEY_GAME_PUBLIC_IP"), System.Net.IPAddress.Loopback);
        _ = Lunarite.Handlers.Agent.ResolveGameAddress(
            Environment.GetEnvironmentVariable("HUXLEY_P2P_TEST_PUBLIC_IP"),
            System.Net.IPAddress.Loopback, "HUXLEY_P2P_TEST_PUBLIC_IP");
        if (Environment.GetEnvironmentVariable("HUXLEY_TRUST_NAT_LOCAL_IP") == "1")
            logger.LogWarning("[Accounts] trusted-NAT local-IP handoff enabled; client-reported addresses are spoofable.");

        // Complete any crash-interrupted multi-store commit before the stores load their files.
        multiStoreTransaction.Recover();
        sqliteStore.ImportLegacyFiles();
        sqliteStore.SplitDocuments();

        serviceProvider.ConfigurePacketHandlers();

        var accountStore = serviceProvider.GetRequiredService<AccountStore>();
        var characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        var playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        var questConditions = serviceProvider.GetRequiredService<QuestConditions>();
        var questEntryReservationStore = serviceProvider.GetRequiredService<QuestEntryReservationStore>();
        var questRewards = serviceProvider.GetRequiredService<QuestRewards>();
        var questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
        var questGateway = serviceProvider.GetRequiredService<QuestGateway>();
        var itemManager = serviceProvider.GetRequiredService<ItemManager>();
        // op56 kill counters are written at most once per QuestStore.KillSaveDelay; write a queued
        // one before the process exits.
        var questStore = serviceProvider.GetRequiredService<QuestStore>();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => questStore.Flush();

        string? resetLogin = Environment.GetEnvironmentVariable("HUXLEY_RESET_ACCOUNT");
        string? resetPassword = Environment.GetEnvironmentVariable("HUXLEY_RESET_PASSWORD");
        if (!string.IsNullOrWhiteSpace(resetLogin) && !string.IsNullOrEmpty(resetPassword))
        {
            if (!accountStore.TryResetPassword(resetLogin, resetPassword, out int resetUserId))
                throw new InvalidOperationException($"Account password reset failed for '{resetLogin}'.");
            logger.LogInformation("[Accounts] reset password for '{0}' (user {1}).", resetLogin, resetUserId);
        }

        // Fail visibly at startup if the extracted quest metadata or the configured client maps
        // cannot be indexed; quest routing must never silently fall back to fabricated map names.
        questCatalog.EnsureLoaded();
        questConditions.EnsureLoaded();
        questRewards.EnsureLoaded();
        playerStore.MigrateLegacyStarterSkills();
        // Quest rooms never survive a restart, so any entry-item reservation still on disk was
        // consumed but never committed; give the items back.
        questEntryReservationStore.RefundAbandoned();
        playerStore.MigrateArmorSprays();
        playerStore.MigrateCorruptMissionDurability();
        if (Environment.GetEnvironmentVariable("QUEST_CATALOG_AUDIT") == "1")
        {
            foreach (var character in characterStore.All())
            {
                var available = questConditions.Available(character.Pid);
                logger.LogInformation("[QuestAudit] PID {0} species={1} style={2} level={3}: {4} possible [{5}]",
                    character.Pid, character.Index, character.Style,
                    playerStore.GetProgress(character.Pid).Level, available.Count, string.Join(",", available));
            }
        }

        foreach (var assemblyType in Assembly.GetExecutingAssembly().GetTypes())
        {
            var handlerAttribute = assemblyType.GetCustomAttribute<HandlerAttribute>();

            var handlerMethod = assemblyType.GetMethod(nameof(Server.OnHandlePacket), BindingFlags.Public | BindingFlags.Static);

            if (handlerAttribute is null || handlerMethod is null)
                continue;

            var handler = handlerMethod.CreateDelegate<Server.OnHandlePacket>();

            _servers.Add(new Server(handlerAttribute.Port, handlerAttribute.Type, handler, loggerFactory));
        }

        // Native NAT classification reports the client's actual bound P2P UDP port only
        // after op127 has supplied two responding test endpoints. Bind them before any
        // Game connection can receive that announcement.
        _p2pNatTestService = new P2PNatTestService(loggerFactory.CreateLogger<P2PNatTestService>());
        _p2pNatTestService.Start();
        serviceProvider.GetRequiredService<P2PManager>().NatTestService = _p2pNatTestService;

        foreach (var server in _servers)
            server.Start();

        logger.LogInformation("Servers successfully started.");

        _cashShopWebServer = new CashShopWebServer(serviceProvider.GetRequiredService<CashShopCatalog>());
        _cashShopWebServer.Start();
        _patchServer = PatchServer.StartFromEnvironment(loggerFactory);
        _serverPingService = ServerPingService.StartFromEnvironment(loggerFactory);
        PlayerLanguage.Start(Path.Combine(AppContext.BaseDirectory, "korean-addresses.txt"),
            loggerFactory.CreateLogger("Language"));

        // Retail item timers are absolute seconds. Keep live inventories authoritative; the
        // selection path independently catches timers that elapsed while a player was offline.
        _itemExpiryTimer = new Timer(_ => itemManager.SweepExpiredItems(), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // Squad members still offline after SquadManager.OfflineMemberGrace leave their squad.
        var squadManager = serviceProvider.GetRequiredService<SquadManager>();
        _squadSweepTimer = new Timer(_ => squadManager.SweepOfflineMembers(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        if (_serverPingService != null)
            _serverPingService.Query = serviceProvider.GetRequiredService<DiscordPresence>().Reply;

        // Total time played per character (PlayerProgress.PlayedSeconds). Counted here rather than at
        // logout so a crash loses a minute at most, and city travel's relinks do not matter.
        var playTimeStore = serviceProvider.GetRequiredService<PlayerStore>();
        var playTimePlayers = serviceProvider.GetRequiredService<PlayerManager>();
        // Same minute: armor now held joins the account's wardrobe (docs/WARDROBE.md).
        var wardrobe = serviceProvider.GetRequiredService<WardrobeManager>();
        wardrobe.BackfillOnce(Environment.GetEnvironmentVariable("HUXLEY_WARDROBE_HISTORY") is { Length: > 0 } history
            ? history : Path.Combine(AppContext.BaseDirectory, "wardrobe-history.json"));
        _playTimeTimer = new Timer(_ =>
        {
            var online = playTimePlayers.OnlinePids.ToArray();
            playTimeStore.AddPlayedSeconds(online, 60);
            wardrobe.CollectOnline(online);
        }, null,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        // HUXLEY_STATUS_FILE: players online per faction, rewritten every 15 s for outside status
        // tools (deploy/linux/alert.sh posts it to Discord). Unset = off.
        if (Environment.GetEnvironmentVariable("HUXLEY_STATUS_FILE") is { Length: > 0 } statusFile)
        {
            var onlinePlayers = serviceProvider.GetRequiredService<PlayerManager>();
            var characters = serviceProvider.GetRequiredService<CharacterStore>();
            var quests = serviceProvider.GetRequiredService<QuestManager>();
            var battles = serviceProvider.GetRequiredService<BattleInstanceManager>();
            _statusFileTimer = new Timer(_ => OnlineStatus.Write(statusFile, onlinePlayers, characters,
                    pid => quests.IsPlayerInQuest(pid) || battles.IsInBattle(pid)), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
            // Same notice as the GM !notice, from outside the game (restart announcements).
            string noticeFile = OnlineStatus.NoticePath(statusFile);
            _noticeFileTimer = new Timer(_ =>
            {
                if (OnlineStatus.TakeNotice(noticeFile) is not { } text) return;
                var notice = new Lunarite.Packets.Outgoing.Message
                    { Kind = Lunarite.Packets.Outgoing.ServerMessageKind.Notice, Text = text, LifeSeconds = 10 };
                foreach (var target in onlinePlayers.OnlineClients) target.SendPacket(notice);
                Console.WriteLine($"[Notice] {text}");
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        // Hourly verified snapshots, in backups/ next to the server's data unless HUXLEY_BACKUP_DIR
        // names another folder; HUXLEY_BACKUP_DIR=off turns them off.
        string backupDirectory = Environment.GetEnvironmentVariable("HUXLEY_BACKUP_DIR") is { Length: > 0 } configuredBackups
            ? configuredBackups : Path.Combine(AppContext.BaseDirectory, "backups");
        if (!string.Equals(backupDirectory, "off", StringComparison.OrdinalIgnoreCase))
        {
            int backupRetention = int.TryParse(Environment.GetEnvironmentVariable("HUXLEY_BACKUP_RETENTION"),
                out int configuredRetention) && configuredRetention > 0 ? configuredRetention : 168;
            void BackupState()
            {
                try
                {
                    string? snapshot = stateBackup.CreateSnapshot(AppContext.BaseDirectory, backupDirectory);
                    if (snapshot != null)
                    {
                        if (stateBackup.VerifySnapshot(snapshot))
                            logger.LogInformation("[Persistence] published verified state snapshot {0}.", snapshot);
                        else
                            logger.LogError("[Persistence] published snapshot failed integrity verification: {0}.", snapshot);
                    }
                    int pruned = stateBackup.PruneSnapshots(backupDirectory, backupRetention);
                    if (pruned != 0) logger.LogInformation("[Persistence] pruned {0} expired state snapshot(s).", pruned);
                }
                catch (Exception ex) { logger.LogError("[Persistence] state snapshot failed: {0}", ex.Message); }
            }
            BackupState();
            _stateBackupTimer = new Timer(_ => BackupState(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        // Gateway handshake test harness: when GATEWAY_TEST=1, connect to a headless dedicated
        // Huxley server (plain-UDP TcpNetDriver on 127.0.0.1:7777) and run the UE3 handshake,
        // no live client needed. Off in normal operation.
        if (Environment.GetEnvironmentVariable("GATEWAY_TEST") == "1")
            questGateway.StartTest();
        if (Environment.GetEnvironmentVariable("GATEWAY_CAPTURE") == "1")
            questGateway.StartCapture();
        if (Environment.GetEnvironmentVariable("GATEWAY_PROXY") == "1")
            questGateway.StartProxy();

        Process.GetCurrentProcess().WaitForExit();
    }

    private static byte ComputeChecksum(ReadOnlySpan<byte> data)
    {
        var size = (data.Length % 2 == 0)
            ? data.Length
            : data.Length - 1;

        var checksum = 0u;

        if (size > 0)
        {
            var count = ((size - 1) / 2) + 1;

            for (var i = 0; i < size; i += sizeof(ushort))
                checksum += (uint)(data[i + 1] ^ (ushort)(data[i] << 8));
        }

        while (checksum >> 16 > 0)
            checksum = (checksum & 0xFFFF) + (checksum >> 16);

        return (byte)~checksum;
    }

    private static void UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (!(e.ExceptionObject is Exception ex))
            return;

        // The whole exception with its stack: this is the only record of a crash.
        Log.WriteError("Unhandled exception{0}: {1}", e.IsTerminating ? " (terminating)" : "", ex);
    }
}
