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
public static class ClanAppointmentHandler
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
        if (!reader.TryRead(out string? name) || !reader.TryRead(out byte grade)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var target = _characterStore.All().FirstOrDefault(x => CharacterStore.SameName(x.Name, name));
        byte result = target == null ? (byte)36 : _clanStore.Appoint(pid, target.Pid, grade);
        client.SendPacket(new ClanActionResult { Opcode = 188, Result = result });
        if (result == 33 && target != null)
        {
            if (Game.TryGetClient(target.Pid, out var targetClient))
                targetClient.SendPacket(new ClanActionResult { Opcode = 188, Result = ClanResultCode.AppointAppointed });
            _clanManager.BroadcastClanHeadline(target.Pid);
            _clanManager.PushClanMyInfo(target.Pid);
        }
        return true;
    }
}
