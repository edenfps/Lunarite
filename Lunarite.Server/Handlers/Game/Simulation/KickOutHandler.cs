using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class KickOutHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int targetPid) || reader.RemainingLength != 0) return false;
        _simulationManager.KickOut(client, targetPid);
        return true;
    }
}
