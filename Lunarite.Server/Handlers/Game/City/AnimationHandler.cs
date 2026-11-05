using System;
using Lunarite.World;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Microsoft.Extensions.DependencyInjection;

namespace Lunarite.Handlers;

[PacketHandler]
public static class AnimationHandler
{
    private static CityManager _cityManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native SendAnimation has one string; server OnAnimation is opcode 26 `ds`.
        if (!reader.TryRead(out string? name) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        if (pid > 0 && Game.PlayerFor(pid).IsSpawned && !string.IsNullOrEmpty(name))
            _cityManager.BroadcastPresence(client, new Animation { Pid = pid, Name = name! });
        return true;
    }
}
