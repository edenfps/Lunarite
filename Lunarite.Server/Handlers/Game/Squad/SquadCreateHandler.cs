using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SquadCreateHandler
{
    private static SquadManager _squadManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? name) || !reader.TryRead(out int options)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte answer = _squadManager.SquadCreate(pid, name, options);
        client.SendPacket(new SquadActionResult { Opcode = 101, Answer = answer });
        if (answer == 0) _squadManager.SendSquadStateAndMembers(client);
        return true;
    }
}
