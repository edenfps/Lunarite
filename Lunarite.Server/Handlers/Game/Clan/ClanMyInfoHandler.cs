using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanMyInfoHandler
{
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var clan = _clanStore.ForPid(pid);
        client.SendPacket(new ClanMyInfo { ClanName = clan?.Name ?? "",
            Grade = _clanStore.Grade(pid), Mark = clan?.Mark ?? 0 });
        return true;
    }
}
