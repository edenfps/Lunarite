using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanPermissionHandler
{
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        byte[] permissions = new byte[10];
        for (int i = 0; i < permissions.Length; i++)
            if (!reader.TryRead(out permissions[i])) return false;
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        client.SendPacket(new ClanActionResult { Opcode = 190, Result = _clanStore.SetPermissions(pid, permissions) });
        return true;
    }
}
