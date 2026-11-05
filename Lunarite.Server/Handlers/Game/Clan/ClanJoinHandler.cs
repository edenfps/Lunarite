using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanJoinHandler
{
    private static ClanManager _clanManager = null!;
    private static CharacterStore _characterStore = null!;
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
        _clanManager = serviceProvider.GetRequiredService<ClanManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte accept) || !reader.TryRead(out string? masterName)
            || !reader.TryRead(out string? clanName) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte result = _clanStore.AnswerInvite(pid, clanName ?? "", accept == 1, out int inviterPid);
        client.SendPacket(new ClanActionResult { Opcode = 177, Result = result });
        if (result == 9 && Game.TryGetClient(inviterPid, out var inviterClient))
            inviterClient.SendPacket(new ClanInvite { Result = ClanResultCode.InviteCanceled,
                PlayerName = _characterStore.Get(pid)?.Name ?? "" });
        if (result == 6)
        {
            _clanManager.BroadcastClanHeadline(pid);
            _clanManager.PushClanMyInfo(pid);
        }
        _ = masterName;
        return true;
    }
}
