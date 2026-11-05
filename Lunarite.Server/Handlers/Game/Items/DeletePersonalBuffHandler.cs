using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class DeletePersonalBuffHandler
{
    private static PersonalBuffStore _personalBuffStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _personalBuffStore = serviceProvider.GetRequiredService<PersonalBuffStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        int pid = Game.PidOf(client);
        if (!reader.TryRead(out int buffId) || reader.RemainingLength != 0 || pid <= 0)
            return false;
        if (_personalBuffStore.TryDelete(pid, buffId, out var buff))
            client.SendPacket(new PersonalBuffResult { Action = BuffResultCode.Deleted, Buff = buff });
        else
            client.SendPacket(new PersonalBuffResult { Action = BuffResultCode.UndefinedBuff });
        return true;
    }
}
