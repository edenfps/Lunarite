using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class LFSInviteAnswerHandler
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
        if (!reader.TryRead(out int squadKey) || !reader.TryRead(out byte answer)
            || reader.RemainingLength != 0 || answer > 1) return false;
        int targetPid = Game.PidOf(client);
        var (joined, leaderPid) = _squadManager.LFSInviteAnswer(targetPid, squadKey, answer);
        byte finalAnswer = joined ? (byte)0 : answer == 0 ? (byte)1 : answer;
        // OnLFSInviteAnswer opens a message box for every op115 but only has text for
        // ANSWER_REFUSE/ANSWER_EXCLUSION, so a consent would show an empty box. The leader
        // learns about the join from the squad-state broadcast instead.
        if (!joined && Game.TryGetClient(leaderPid, out var leader))
            leader.SendPacket(new LFSInviteAnswer { Answer = finalAnswer, Pid = targetPid,
                PlayerName = _characterStore.Get(targetPid)?.Name ?? "" });
        if (joined) _squadManager.BroadcastSquadState(targetPid);
        return true;
    }
}
