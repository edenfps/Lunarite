using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class CityWarpHandler
{
    private static CharacterManager _characterManager = null!;
    private static CityManager _cityManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        int pid = Game.PidOf(client);
        if (!reader.TryRead(out int price) || reader.RemainingLength != 0 || pid <= 0)
            return false;
        byte result = _cityManager.ApplyCityWarpCost(pid, price);
        client.SendPacket(new CityWarp { Result = result });
        if (result == 0 && price >= 0)
            _characterManager.SendMoneyParameter(client, pid);
        return true;
    }
}
