using System;
using Lunarite.World;

using Lunarite.Network;
using Microsoft.Extensions.DependencyInjection;

namespace Lunarite.Handlers;

[PacketHandler]
public static class JoinRoomHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte roomId) || !reader.TryRead(out string? password) || reader.RemainingLength != 0)
            return false;
        return _simulationManager.JoinRoom(client, roomId, password ?? "");
    }
}
