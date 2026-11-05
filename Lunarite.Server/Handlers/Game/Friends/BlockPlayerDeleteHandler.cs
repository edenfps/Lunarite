using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BlockPlayerDeleteHandler
{
    private static BlockStore _blockStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _blockStore = serviceProvider.GetRequiredService<BlockStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int targetPid) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        client.SendPacket(new BlockActionResult { Opcode = 211, Result = _blockStore.Delete(pid, targetPid) });
        return true;
    }
}
