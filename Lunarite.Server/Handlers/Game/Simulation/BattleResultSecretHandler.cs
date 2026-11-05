using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleResultSecretHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        _simulationManager.BattleResultSecret(client);
        return true;
    }
}
