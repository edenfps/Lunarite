using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransmitGetItemHandler
{
    private static TransmitManager _transmitManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _transmitManager = serviceProvider.GetRequiredService<TransmitManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!TransmitGetItemRequest.TryDeserialize(ref reader, out var packet))
            return false;

        int pid = Game.PidOf(client);
        _transmitManager.TransmitGetItem(client, pid, packet);
        return true;
    }
}
