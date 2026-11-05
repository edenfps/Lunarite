using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class LFSPlayerInfoHandler
{
    private static SquadManager _squadManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int targetPid) || reader.RemainingLength != 0) return false;
        bool exists = _squadManager.IsInLfs(targetPid);
        client.SendPacket(new LFSPlayerInfo { Answer = exists ? (byte)0 : (byte)1,
            Entry = exists ? _squadManager.BuildLFSEntry(targetPid) : default });
        return true;
    }
}
