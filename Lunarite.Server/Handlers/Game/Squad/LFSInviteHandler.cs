using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class LFSInviteHandler
{
    private static SquadManager _squadManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int targetPid) || reader.RemainingLength != 0) return false;
        int leaderPid = Game.PidOf(client);
        var (invite, blocked) = _squadManager.LFSInvite(leaderPid, targetPid);
        if (invite is not var (squadKey, squadName))
        {
            client.SendPacket(_squadManager.BuildLFSInviteFailure(targetPid, blocked));
            return true;
        }
        if (Game.TryGetClient(targetPid, out var target))
            target.SendPacket(new LFSInvite { Answer = 0,
                SquadKey = squadKey, SquadName = squadName });
        else
        {
            _squadManager.ForgetLfsInvite(squadKey, targetPid);
            client.SendPacket(_squadManager.BuildLFSInviteFailure(targetPid, false));
        }
        return true;
    }
}
