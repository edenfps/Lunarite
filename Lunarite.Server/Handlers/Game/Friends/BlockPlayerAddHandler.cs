using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BlockPlayerAddHandler
{
    private static BlockStore _blockStore = null!;
    private static CharacterStore _characterStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _blockStore = serviceProvider.GetRequiredService<BlockStore>();
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? name) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var target = _characterStore.All().FirstOrDefault(x => CharacterStore.SameName(x.Name, name));
        byte result = target == null ? (byte)3 : _blockStore.Add(pid, target.Pid);
        client.SendPacket(new BlockActionResult { Opcode = 210, Result = result });
        return true;
    }
}
