using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanInviteHandler
{
    private static CharacterStore _characterStore = null!;
    private static ClanStore _clanStore = null!;
    private static PlayerManager _playerManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? targetName) || reader.RemainingLength != 0) return false;
        int leaderPid = Game.PidOf(client);
        var target = _characterStore.All()
            .FirstOrDefault(x => CharacterStore.SameName(x.Name, targetName));
        var clan = _clanStore.ForPid(leaderPid);
        bool targetOnline = target != null && _playerManager.IsOnline(target.Pid);
        var leader = _characterStore.Get(leaderPid);
        byte result = target == null || !targetOnline ? (byte)10
            : leader == null || !ClanManager.CanShareClan(leader.Index, target.Index) ? (byte)52
            : _clanStore.Invite(leaderPid, target.Pid);
        // The inviter's own 7 carries empty names, which ProcClanInvite drops; real names would
        // open the invitation popup on the inviter's screen.
        client.SendPacket(new ClanInvite { Result = result, PlayerName = target?.Name ?? "" });
        if (result == 7 && target != null && Game.TryGetClient(target.Pid, out var targetClient))
            targetClient.SendPacket(new ClanInvite { Result = ClanResultCode.InviteSuccess,
                MasterName = leader?.Name ?? "", ClanName = clan?.Name ?? "" });
        return true;
    }
}
