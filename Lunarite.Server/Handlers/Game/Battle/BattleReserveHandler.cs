using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleReserveHandler
{
    private static BattleFront _battleFront = null!;
    private static BattleManager _battleManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
        _battleFront = serviceProvider.GetRequiredService<BattleFront>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte axis) || !reader.TryRead(out byte reserve)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte answer = pid > 0 && reserve <= 1 && (reserve == 0 || _battleFront.Axes.Contains(axis))
            ? (byte)0 : (byte)1;
        if (answer == 0)
        {
            if (reserve != 0) answer = _battleManager.Reserve(pid, axis);
            else _battleManager.CancelReservation(pid);
        }
        client.SendPacket(new BattleReserve { Axis = axis, Reserved = reserve, Answer = answer });
        // An axis whose battleground is already open invites at once instead of at the next sweep.
        if (answer == 0 && reserve != 0) _battleManager.InviteReservations(axis);
        return true;
    }
}
