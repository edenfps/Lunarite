using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class VanSquadMemberHandler
{
    private static SquadManager _squadManager = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int targetPid) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var (answer, squadName) = _squadManager.VanSquadMember(pid, targetPid);
        client.SendPacket(new SquadActionResult { Opcode = 107, Answer = answer });
        if (answer == 0)
        {
            if (Game.TryGetClient(targetPid, out var target))
            {
                target.SendPacket(new SquadApplicationAnswer { Answer = ConsentAnswer.Exclusion,
                    LeaderName = _characterStore.Get(pid)?.Name ?? "", SquadName = squadName });
                target.SendPacket(new SquadState { State = 0 });
                target.SendPacket(new EmptySquadMemberList());
            }
            _squadManager.BroadcastSquadState(pid);
        }
        return true;
    }
}
