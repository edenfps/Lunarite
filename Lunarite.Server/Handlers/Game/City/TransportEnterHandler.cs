using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransportEnterHandler
{
    private static CityManager _cityManager = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native SendTransportEnter is op104 `b`; server op118 returns one answer byte.
        if (!reader.TryRead(out byte index) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte answer = _cityManager.TransportEnter(pid, index);
        client.SendPacket(new TransportEnter { Answer = answer });
        if (answer == 0)
            client.SendPacket(new Parameter
            {
                Param = Parameter.PARAMETER_MONEY,
                Value = (int)Math.Min(int.MaxValue, _playerStore.GetProgress(pid).Money)
            });
        return true;
    }
}
