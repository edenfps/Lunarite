using System;
using Lunarite.World;

using Lunarite.Network;
using Microsoft.Extensions.DependencyInjection;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleEnterHandler
{
    private static BattleManager _battleManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // SendBattleEnter is client op27 `b` carrying BattleServerID. Native success is
        // op31 `bdwm[16]`, which travels the client to a dedicated-server commandlet.
        if (!reader.TryRead(out byte serverId) || reader.RemainingLength != 0) return false;
        _battleManager.SendBattleAdmission(client, serverId);
        return true;
    }
}
