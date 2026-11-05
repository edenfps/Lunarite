using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleBackendTimeHandler
{
    private static BattleServerManager _battleServerManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int remainingSeconds) || reader.RemainingLength != 0
            || !_battleServerManager.TryGetServer(client.Id, out var server)) return false;
        if (remainingSeconds < 0 || remainingSeconds > 24 * 60 * 60) return true;
        _battleServerManager.SetServer(client.Id, server with { RemainingSeconds = remainingSeconds });
        return true;
    }
}
