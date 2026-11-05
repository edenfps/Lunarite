using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ClanNoticeInfoHandler
{
    private static ClanStore _clanStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _clanStore = serviceProvider.GetRequiredService<ClanStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native SendClanNoticeInfo is client op166 with no payload, sent by the chat scene on init.
        if (reader.RemainingLength != 0) return false;
        // It has no delegate, so answer with op182 19+notice, which posts itself to clan chat.
        int pid = Game.PidOf(client);
        var clan = _clanStore.ForPid(pid);
        if (clan != null && !string.IsNullOrWhiteSpace(clan.Notice))
            client.SendPacket(new ClanNotice { Result = ClanResultCode.NoticeSuccess, Notice = clan.Notice });
        return true;
    }
}
