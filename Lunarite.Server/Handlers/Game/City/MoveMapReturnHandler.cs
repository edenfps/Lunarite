using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class MoveMapReturnHandler
{
    private static CityManager _cityManager = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // SendMoveMapReturn is native op139 with no payload. (op28 is BattleLeave.)
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        _cityManager.LeaveCurrentPresence(client, pid);
        byte characterIndex = _characterStore.Get(pid)?.Index ?? 1;
        (int Map, int Area)? previous = Game.PlayerFor(pid).TakeMoveMapReturn();
        var destination = _cityManager.ResolveMoveMapReturn(previous, characterIndex);
        Game.PlayerFor(pid).CityMap = destination.Map;
        _characterStore.SetCityLocation(pid, destination.Map, destination.Area);
        CityManager.ArmExpectedCityTravel(client);
        client.SendPacket(new LoadMap
        {
            Index = destination.Map,
            Position = destination.Area
        });
        return true;
    }
}
