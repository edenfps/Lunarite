using System;
using Lunarite.World;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Microsoft.Extensions.DependencyInjection;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleFieldInfosHandler
{
    private static BattleManager _battleManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // SendBattleFieldInfos is op31 `bm`; each pair is axis then map on the wire.
        if (!reader.TryRead(out byte count) || reader.RemainingLength != count * 2) return false;
        var response = new BattleFieldInfos();
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryRead(out byte axis) || !reader.TryRead(out byte map)) return false;
            response.Entries.Add(_battleManager.BuildBattleFieldEntry(axis, map));
        }
        client.SendPacket(response);
        return true;
    }
}
