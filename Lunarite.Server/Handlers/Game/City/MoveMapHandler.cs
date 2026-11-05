using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class MoveMapHandler
{
    private static CityZoneCatalog _cityZoneCatalog = null!;
    private static P2PManager _p2pManager = null!;
    private static QuestManager _questManager = null!;
    private static CityManager _cityManager = null!;
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MoveMapHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _cityZoneCatalog = serviceProvider.GetRequiredService<CityZoneCatalog>();
        _p2pManager = serviceProvider.GetRequiredService<P2PManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // SendMoveMap and SendMoveMapSilent are op20/op138, both native `bb`.
        if (!reader.TryRead(out byte mapWire) || !reader.TryRead(out byte positionWire)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        int mapIndex = CityManager.DecodeMapByte(mapWire);
        int position = CityManager.DecodeMapByte(positionWire);
        var character = _characterStore.Get(pid);
        // A tutorial zone's Level is a quest index (810101); browsing it as a map makes the client
        // assert in UnGame.cpp (GWorld). Tutorials start through op42, never through MoveMap.
        if (!_cityZoneCatalog.IsValidArea(mapIndex, position, character?.Index ?? 0) || _questManager.IsQuestLevelZone(mapIndex))
        {
            var saved = _characterStore.GetCityLocation(pid);
            var retained = _p2pManager.RetainedCityDestination(
                (Game.PlayerFor(pid).CityMap ?? saved.Map), saved, character?.Index ?? 1);
            _logger.LogWarning(
                "[City] rejected invalid destination map {0}, area {1} requested by PID {2}; retaining map {3}, area {4}.",
                mapIndex, position, pid, retained.Map, retained.Area);
            CityManager.ArmExpectedCityTravel(client);
            client.SendPacket(new LoadMap { Index = retained.Map, Position = retained.Area });
            return true;
        }
        _cityManager.MoveMap(client, pid, mapIndex, position);
        return true;
    }
}
