using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestShareHandler
{
    private static QuestCatalog _questCatalog = null!;
    private static QuestManager _questManager = null!;
    private static SquadManager _squadManager = null!;
    private static QuestConditions _questConditions = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _questConditions = serviceProvider.GetRequiredService<QuestConditions>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int questIndex) || reader.RemainingLength != 0)
            return false;

        int sharerPid = Game.PidOf(client);
        var quest = _questCatalog.Get(questIndex);
        if (sharerPid <= 0 || quest == null || !_questStore.Acquired(sharerPid).Contains(questIndex))
        {
            client.SendPacket(new QuestShare
            {
                Pid = sharerPid, QuestIndex = questIndex, Answer = QuestResultCode.Notacquire
            });
            return true;
        }
        if (quest.ShareType.Equals("Share_None", StringComparison.OrdinalIgnoreCase))
        {
            client.SendPacket(new QuestShare
            {
                Pid = sharerPid, QuestIndex = questIndex, Answer = QuestResultCode.NoneShare
            });
            return true;
        }

        List<int> members = _squadManager.SquadMembersOf(sharerPid);
        if (members.Count == 0)
        {
            client.SendPacket(new QuestShare
            {
                Pid = sharerPid, QuestIndex = questIndex, Answer = QuestResultCode.NoneSquad
            });
            return true;
        }

        client.SendPacket(new QuestShare { Pid = sharerPid, QuestIndex = questIndex, Answer = QuestResultCode.Ok });
        foreach (int memberPid in members.Where(x => x != sharerPid))
        {
            byte result;
            if (_questStore.Completed(memberPid).Contains(questIndex)) result = QuestResultCode.AlreadyComplete;
            else if (_questStore.Acquired(memberPid).Contains(questIndex)) result = QuestResultCode.Alreadyacquire;
            else if (!_questConditions.Available(memberPid).Contains(questIndex)) result = QuestResultCode.Impossible;
            else if (Game.TryGetClient(memberPid, out var memberClient))
            {
                _questManager.SetPendingShare(memberClient.Id, (sharerPid, questIndex));
                memberClient.SendPacket(new QuestShareRequest { SharerPid = sharerPid, QuestIndex = questIndex });
                continue;
            }
            else result = QuestResultCode.NotfoundPlayer;

            client.SendPacket(new QuestShare { Pid = memberPid, QuestIndex = questIndex, Answer = result });
        }
        return true;
    }
}
