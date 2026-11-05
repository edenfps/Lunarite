using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SquadSecessionHandler
{
    private static SquadManager _squadManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var (remaining, answer) = _squadManager.SquadSecession(pid);
        client.SendPacket(new SquadActionResult { Opcode = 103, Answer = answer });
        if (answer == 0)
        {
            client.SendPacket(new SquadState { State = 0 });
            client.SendPacket(new EmptySquadMemberList());
            foreach (int memberPid in remaining) _squadManager.BroadcastSquadState(memberPid);
        }
        return true;
    }
}
