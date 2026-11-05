using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

// The listen host reports a guest's completed UE3/P2P login as client op109
// `db` (PID, SC_PLAYER_STATE_*). Retail reflects it to room UIs through
// server op142 / OnSimulPlayerStateBusy.
[PacketHandler]
public static class SimulationJoinedPlayerStateHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int memberPid) || !reader.TryRead(out byte state)
            || reader.RemainingLength != 0) return false;
        _simulationManager.SimulationJoinedPlayerState(client, memberPid, state);
        return true;
    }
}
