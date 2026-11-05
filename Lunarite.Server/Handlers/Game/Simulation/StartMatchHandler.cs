using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

// HuxleyGame.MatchInProgress.BeginState sends this once the listen host has begun the match.
// It is a status report, not a request to launch.
[PacketHandler]
public static class StartMatchHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        _simulationManager.StartMatch(client);
        return true;
    }
}
