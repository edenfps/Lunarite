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
public static class ClanFireHandler
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
        var target = _characterStore.All().FirstOrDefault(x => CharacterStore.SameName(x.Name, name));
        byte result = target == null ? (byte)22 : _clanStore.Fire(pid, target.Pid);
        client.SendPacket(new ClanActionResult { Opcode = 184, Result = result });
        if (result == 23 && target != null && Game.TryGetClient(target.Pid, out var targetClient))
            targetClient.SendPacket(new ClanActionResult { Opcode = 184, Result = ClanResultCode.FireFired });
        if (result == 23 && target != null)
        {
            _clanManager.BroadcastClanHeadline(target.Pid);
            _clanManager.PushClanMyInfo(target.Pid);
        }
        return true;
    }
}
