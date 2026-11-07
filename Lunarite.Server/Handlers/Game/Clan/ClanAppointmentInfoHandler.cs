using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanAppointmentInfoHandler
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
        if (!reader.TryRead(out string? name) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var target = _characterStore.All().FirstOrDefault(x => CharacterStore.SameName(x.Name, name));
        var counts = _clanStore.AppointmentCounts(target?.Pid ?? pid);
        client.SendPacket(new ClanAppointmentInfo
        {
            Result = ClanResultCode.AppointInfoSuccess, SubMaster = counts.SubMaster,
            SquadMaster = counts.SquadMaster, SubSquadMaster = counts.SubSquadMaster
        });
        return true;
    }
}
