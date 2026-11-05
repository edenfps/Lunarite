using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleAxisInfosHandler
{
    private static BattleManager _battleManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // SendBattleAxisInfos is op30 `bm`: byte count plus one byte per axis ID.
        if (!reader.TryRead(out byte count) || reader.RemainingLength != count) return false;
        var response = new BattleAxisInfos();
        int pid = Game.PidOf(client);
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryRead(out byte axis)) return false;
            response.Entries.Add(_battleManager.BuildBattleAxisEntry(axis, pid));
        }
        client.SendPacket(response);
        return true;
    }
}
