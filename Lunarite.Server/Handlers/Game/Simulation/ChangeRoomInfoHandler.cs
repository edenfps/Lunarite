using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ChangeRoomInfoHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native SendChangeRoomInfo: dbwbbb.
        if (!reader.TryRead(out int map) || !reader.TryRead(out byte gameType)
            || !reader.TryRead(out ushort goalScore) || !reader.TryRead(out byte goalTime)
            || !reader.TryRead(out byte maxPlayers) || !reader.TryRead(out byte limitClass)
            || reader.RemainingLength != 0) return false;
        _simulationManager.ChangeRoomInfo(client, map, gameType, goalScore, goalTime, maxPlayers, limitClass);
        return true;
    }
}
