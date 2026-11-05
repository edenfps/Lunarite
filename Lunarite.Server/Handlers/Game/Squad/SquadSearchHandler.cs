using System;
using Lunarite.World;

using Lunarite.Network;
using Microsoft.Extensions.DependencyInjection;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SquadSearchHandler
{
    private static SquadManager _squadManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? keyword) || reader.RemainingLength != 0) return false;
        _squadManager.SendSquadList(client, 4, keyword ?? "");
        return true;
    }
}
