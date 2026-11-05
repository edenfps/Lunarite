using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanInfoHandler
{
    private static CharacterStore _characterStore = null!;
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var clan = _clanStore.ForPid(pid);
        int masterPid = clan?.Members.FirstOrDefault(x => x.Grade == 9)?.Pid ?? 0;
        client.SendPacket(new ClanInfo { Result = clan == null ? (byte)17 : (byte)16,
            ClanName = clan?.Name ?? "", MasterName = _characterStore.Get(masterPid)?.Name ?? "",
            Notice = clan?.Notice ?? "", Mark = clan?.Mark ?? 0,
            Contribution = clan?.Contribution ?? 0 });
        return true;
    }
}
