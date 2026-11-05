using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanDuplicateCheckHandler
{
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? name) || reader.RemainingLength != 0) return false;
        client.SendPacket(new ClanActionResult { Opcode = 183,
            Result = _clanStore.NameExists(name ?? "") ? (byte)20 : (byte)21 });
        return true;
    }
}
