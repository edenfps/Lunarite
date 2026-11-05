using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class RequestCraftRecipeHandler
{
    private static CraftStore _craftStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _craftStore = serviceProvider.GetRequiredService<CraftStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        client.SendPacket(new RequestCraftRecipe { Recipes = _craftStore.Learned(pid) });
        return true;
    }
}
