using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class CreateRoomHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native SendCreateRoom: sbdbbwbbs.
        if (!reader.TryRead(out string? roomName)
            || !reader.TryRead(out byte maxPlayers)
            || !reader.TryRead(out int map)
            || !reader.TryRead(out byte gameType)
            || !reader.TryRead(out byte goalTime)
            || !reader.TryRead(out ushort goalScore)
            || !reader.TryRead(out byte isPublic)
            || !reader.TryRead(out byte limitClass)
            || !reader.TryRead(out string? password)
            || reader.RemainingLength != 0)
            return false;
        _simulationManager.CreateRoom(client, roomName, maxPlayers, map, gameType, goalTime, goalScore, isPublic, limitClass, password);
        return true;
    }
}
