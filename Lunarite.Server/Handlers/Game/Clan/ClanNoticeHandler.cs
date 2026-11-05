using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanNoticeHandler
{
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out string? notice) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        byte result = _clanStore.SetNotice(pid, notice ?? "");
        if (result == 19)
            ClanManager.SendToOnlineClan(_clanStore.MemberPids(pid),
                new ClanNotice { Result = ClanResultCode.NoticeSuccess, Notice = notice ?? "" });
        else
            client.SendPacket(new ClanNotice { Result = result });
        return true;
    }
}
