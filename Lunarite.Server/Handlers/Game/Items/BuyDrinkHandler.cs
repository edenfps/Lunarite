using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BuyDrinkHandler
{
    private static PersonalBuffCatalog _personalBuffCatalog = null!;
    private static CharacterManager _characterManager = null!;
    private static ItemManager _itemManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _personalBuffCatalog = serviceProvider.GetRequiredService<PersonalBuffCatalog>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int setterId) || reader.RemainingLength != 0)
            return false;
        int pid = Game.PidOf(client);
        var setter = _personalBuffCatalog.GetSetter(setterId);
        byte result = setter == null ? (byte)1 : _itemManager.TryChargeDrink(pid, setter.Price);
        if (result == 0)
        {
            client.Session.PendingDrinkSetter = setterId;
            if (setter!.Price > 0) _characterManager.SendMoneyParameter(client, pid);
        }
        client.SendPacket(new BuyDrink { Result = result });
        return true;
    }
}
