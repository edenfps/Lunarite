using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanPermissionInfoHandler
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
        client.SendPacket(new ClanPermissionInfo
        {
            Result = ClanResultCode.PermissonInfoSuccess, Permissions = _clanStore.GetPermissions(pid)
        });
        return true;
    }
}
