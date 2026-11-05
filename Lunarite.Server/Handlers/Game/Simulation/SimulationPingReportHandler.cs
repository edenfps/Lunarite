using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

// FP2PSocket reports a completed room-peer probe as client op194 `bd`:
// PingType (0/1 for Simulation peers) and the computed grade (0..3; 4 is
// the native unknown/unreachable sentinel). Retail forwards the resulting
// PID/grade table to the room owner through server op224 OwnerPingTime.
[PacketHandler]
public static class SimulationPingReportHandler
{
    private static SimulationManager _simulationManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte pingType) || !reader.TryRead(out int pingGrade)
            || reader.RemainingLength != 0)
            return false;
        _simulationManager.SimulationPingReport(client, pingType, pingGrade);
        return true;
    }
}
