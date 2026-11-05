using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class LFSJoinHandler
{
    private static SquadManager _squadManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? comment) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte answer = _squadManager.LFSJoin(pid, comment);
        client.SendPacket(new LFSActionResult { Opcode = 111, Answer = answer });
        if (answer == 0) client.SendPacket(new SquadState { State = 2 });
        return true;
    }
}
