using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SquadApplicationAnswerHandler
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
        if (!reader.TryRead(out byte answer) || !reader.TryRead(out int applicantPid)
            || !reader.TryRead(out int key) || reader.RemainingLength != 0 || answer > 2) return false;
        int leaderPid = Game.PidOf(client);
        var (valid, squadName, joined, joinFailure) = _squadManager.SquadApplicationAnswer(leaderPid, key, applicantPid, answer);
        if (!valid) return true;
        if (Game.TryGetClient(applicantPid, out var applicant))
        {
            // An accepted application ends the applicant's op102 wait. A refusal or exclusion is
            // shown by OnSquadApplicationAnswer alone: its refusal branch returns early so that no
            // message box appears, which an op102 error answer would add.
            if (answer == 0)
                applicant.SendPacket(new SquadActionResult { Opcode = 102, Answer = joined ? (byte)0 : joinFailure });
            else
                applicant.SendPacket(new SquadApplicationAnswer { Answer = answer,
                    LeaderName = _characterStore.Get(leaderPid)?.Name ?? "", SquadName = squadName });
        }
        if (joined) _squadManager.BroadcastSquadState(applicantPid);
        return true;
    }
}
