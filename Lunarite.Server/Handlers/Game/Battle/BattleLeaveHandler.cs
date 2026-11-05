using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleLeaveHandler
{
    private static BattleInstanceManager _battleInstanceManager = null!;
    private static CityZoneCatalog _cityZoneCatalog = null!;
    private static CharacterStore _characterStore = null!;
    private static BattleManager _battleManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {

        _cityZoneCatalog = serviceProvider.GetRequiredService<CityZoneCatalog>();

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
        _battleInstanceManager = serviceProvider.GetRequiredService<BattleInstanceManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        if (pid <= 0) return true;
        _battleManager.CancelReservation(pid);
        _battleInstanceManager.Leave(pid);
        // DedicatedServerLeave (16 s after ClientGameEnded, or the City command) destroys the net
        // driver before sending op28; the client then waits for the city to load. Battle travel
        // keeps the saved city location, so the player returns to where they entered from.
        var (map, area) = _characterStore.GetCityLocation(pid);
        byte characterIndex = _characterStore.Get(pid)?.Index ?? 1;
        if (map < 0 || !_cityZoneCatalog.IsValidArea(map, area, characterIndex))
            (map, area) = (CharacterStore.TrainingCenterMap(characterIndex), 0);
        Game.PlayerFor(pid).CityMap = map;
        CityManager.ArmExpectedCityTravel(client);
        client.SendPacket(new LoadMap { Index = map, Position = area });
        return true;
    }
}
