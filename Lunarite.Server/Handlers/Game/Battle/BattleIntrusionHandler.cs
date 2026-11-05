using System;
using Lunarite.World;

using Lunarite.Network;
using Microsoft.Extensions.DependencyInjection;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleIntrusionHandler
{
    private static BattleManager _battleManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // SendBattleIntrusion is client op29 `b` and shares the same native op31 admission.
        if (!reader.TryRead(out byte serverId) || reader.RemainingLength != 0) return false;
        _battleManager.SendBattleAdmission(client, serverId);
        return true;
    }
}
