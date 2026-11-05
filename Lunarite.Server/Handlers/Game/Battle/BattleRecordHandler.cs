using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleRecordHandler
{
    private static BattleResultStore _battleResultStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _battleResultStore = serviceProvider.GetRequiredService<BattleResultStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte sheet) || reader.RemainingLength != 0 || sheet is < 1 or > 4) return false;
        // ProcCharBattleRecord requires the native struct sizes. All-zero records are valid for a
        // character with no completed PvP rounds.
        client.SendPacket(new CharBattleRecord { Sheet = sheet,
            Data = _battleResultStore.BuildRecordSheet(Game.PidOf(client), sheet) });
        return true;
    }
}
