using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanSecedeHandler
{
    private static ClanManager _clanManager = null!;
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
        _clanManager = serviceProvider.GetRequiredService<ClanManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte result = _clanStore.Leave(pid);
        client.SendPacket(new ClanActionResult { Opcode = 178, Result = result });
        if (result == 11)
        {
            _clanManager.BroadcastClanHeadline(pid);
            _clanManager.PushClanMyInfo(pid);
        }
        return true;
    }
}
