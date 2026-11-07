using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class FriendAddHandler
{
    private static CharacterStore _characterStore = null!;
    private static FriendStore _friendStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _friendStore = serviceProvider.GetRequiredService<FriendStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? name) || !reader.TryRead(out byte relation)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var target = _characterStore.All().FirstOrDefault(x => CharacterStore.SameName(x.Name, name));
        byte result = target == null ? (byte)1 : _friendStore.Add(pid, target.Pid, relation);
        client.SendPacket(new FriendActionResult { Opcode = 158, Result = result });
        return true;
    }
}
