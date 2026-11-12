using System;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServerServices(this IServiceCollection services)
    {
        services.AddLogging(builder => builder
            .AddProvider(new LogProvider())
            .SetMinimumLevel(LogLevel.Debug));

        // Client data tables (data/)
        services.AddSingleton<BattleZoneCatalog>();
        services.AddSingleton<CityTransportCatalog>();
        services.AddSingleton<CashShopCatalog>();
        services.AddSingleton<CityZoneCatalog>();
        services.AddSingleton<CraftCatalog>();
        services.AddSingleton<CraftDrops>();
        services.AddSingleton<ItemCatalog>();
        services.AddSingleton<ItemGroupCatalog>();
        services.AddSingleton<LicenseCatalog>();
        services.AddSingleton<PersonalBuffCatalog>();
        services.AddSingleton<QuestCatalog>();
        services.AddSingleton<QuestLoot>();
        services.AddSingleton<RankTable>();

        // Persistence
        services.AddSingleton<SqliteStore>();
        services.AddSingleton<AtomicJson>();
        services.AddSingleton<MultiStoreTransaction>();
        services.AddSingleton<StateBackup>();

        // Persistent stores
        services.AddSingleton<AccountStore>();
        services.AddSingleton<AccountSessionStore>();
        services.AddSingleton<AuctionStore>();
        services.AddSingleton<BattleResultStore>();
        services.AddSingleton<BlockStore>();
        services.AddSingleton<WardrobeStore>();
        services.AddSingleton<CharacterStore>();
        services.AddSingleton<ClanStore>();
        services.AddSingleton<CraftStore>();
        services.AddSingleton<DurativeItemStore>();
        services.AddSingleton<FriendStore>();
        services.AddSingleton<LicenseStore>();
        services.AddSingleton<PayItemStore>();
        services.AddSingleton<PersonalBuffStore>();
        services.AddSingleton<PlayerStore>();
        services.AddSingleton<QuestEntryReservationStore>();
        services.AddSingleton<QuestStore>();
        services.AddSingleton<TransmitStore>();

        // World state
        services.AddSingleton<PlayerManager>();
        services.AddSingleton<QuestRoomMembers>();
        services.AddSingleton<BattleFront>();
        services.AddSingleton<BattleInstanceManager>();
        services.AddSingleton<BattleServerManager>();
        services.AddSingleton<QuestGateway>();
        services.AddSingleton<ServerEvents>();
        services.AddSingleton<SimulationMapOverrides>();

        // Features
        services.AddSingleton<BattleManager>();
        services.AddSingleton<CharacterManager>();
        services.AddSingleton<CityManager>();
        services.AddSingleton<ClanManager>();
        services.AddSingleton<GmCommands>();
        services.AddSingleton<ItemManager>();
        services.AddSingleton<WardrobeManager>();
        services.AddSingleton<P2PManager>();
        services.AddSingleton<QuestInstanceManager>();
        services.AddSingleton<QuestManager>();
        services.AddSingleton<SimulationManager>();
        services.AddSingleton<DiscordPresence>();
        services.AddSingleton<SquadManager>();
        services.AddSingleton<AuctionManager>();
        services.AddSingleton<TradeManager>();
        services.AddSingleton<TransmitManager>();

        // Rules over the stores
        services.AddSingleton<CharacterDataLifecycle>();
        services.AddSingleton<EconomyRates>();
        services.AddSingleton<EquipmentRules>();
        services.AddSingleton<QuestConditions>();
        services.AddSingleton<QuestRewards>();

        return services;
    }

    // Static classes (the packet handlers and their dispatchers, Sanctuary's [PacketHandler]
    // convention, and the few static helpers that read services) take their services in a
    // ConfigureServices(IServiceProvider) method.
    public static void ConfigurePacketHandlers(this IServiceProvider serviceProvider)
    {
        foreach (var type in typeof(ServiceCollectionExtensions).Assembly.GetTypes())
        {
            if (!type.IsAbstract || !type.IsSealed)
                continue;

            type.GetMethod(HandlerAttribute.ConfigureMethodName, BindingFlags.Public | BindingFlags.Static,
                [typeof(IServiceProvider)])?.Invoke(null, [serviceProvider]);
        }
    }
}
