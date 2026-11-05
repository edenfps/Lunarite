using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.World;

namespace Lunarite.Handlers;

// Periodic battle recovery data from the listen host, client op152 `wwwbm`: two team
// scores, remaining seconds, mode-specific backup type, then 31-byte player records.
// `m` has no count prefix.
[PacketHandler]
public static class BattleGameDataHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!BattleGameDataRequest.TryDeserialize(ref reader, out var report)) return false;
        _simulationManager.BattleGameData(client, report);
        return true;
    }
}
