using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class DelegateLeaderPowerHandler
{
    private static SquadManager _squadManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int targetPid) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte answer = _squadManager.DelegateLeaderPower(pid, targetPid);
        client.SendPacket(new SquadActionResult { Opcode = 106, Answer = answer });
        if (answer == 0) _squadManager.BroadcastSquadState(targetPid);
        return true;
    }
}
