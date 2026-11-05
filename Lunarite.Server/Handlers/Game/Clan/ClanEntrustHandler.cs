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
public static class ClanEntrustHandler
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
        if (!reader.TryRead(out string? name) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var target = _characterStore.All().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        byte result = target == null ? (byte)27 : _clanStore.Entrust(pid, target.Pid);
        client.SendPacket(new ClanActionResult { Opcode = 186, Result = result });
        if (result == 26 && target != null)
        {
            // 28 "you are now the clan leader" to the new master, 29 to everyone else.
            if (Game.TryGetClient(target.Pid, out var targetClient))
                targetClient.SendPacket(new ClanActionResult { Opcode = 186, Result = ClanResultCode.EntrustEntrusted });
            foreach (int memberPid in _clanStore.MemberPids(target.Pid))
                if (memberPid != pid && memberPid != target.Pid && Game.TryGetClient(memberPid, out var memberClient))
                    memberClient.SendPacket(new ClanActionResult { Opcode = 186, Result = ClanResultCode.EntrustNewmaster });
            _clanManager.BroadcastClanHeadline(pid);
            _clanManager.BroadcastClanHeadline(target.Pid);
            _clanManager.PushClanMyInfo(pid);
            _clanManager.PushClanMyInfo(target.Pid);
        }
        return true;
    }
}
