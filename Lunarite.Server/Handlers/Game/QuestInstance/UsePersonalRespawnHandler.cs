using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class UsePersonalRespawnHandler
{
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        int pid = Game.PidOf(client);
        if (reader.RemainingLength != 0 || pid <= 0) return false;
        // Native SendUsePersonalRespawn has no body and no answer callback; the instance has
        // already respawned the player. PARAMETER_RESPAWNCOUNT updates the displayed count.
        if (_playerStore.TryUsePersonalRespawn(pid))
            client.SendPacket(new Parameter
            {
                Param = Parameter.PARAMETER_RESPAWNCOUNT,
                Value = _playerStore.GetProgress(pid).RespawnCount
            });
        return true;
    }
}
