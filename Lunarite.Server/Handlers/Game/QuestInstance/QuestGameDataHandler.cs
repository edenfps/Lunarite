using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.World;

namespace Lunarite.Handlers;

// The native SendQuestGameData loop sends one op152 record per controller in the hosted
// world. Stored per participant; StockItem is world state and grants no inventory.
[PacketHandler]
public static class QuestGameDataHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestGameDataHandler));
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestGameDataRequest.TryDeserialize(ref reader, out var packet))
            return false;
        int questIndex = _questRoomMembers.QuestOf(client.Id);
        int instanceToken = _questRoomMembers.InstanceOf(client.Id);
        int ownerPid = questIndex > 0 && instanceToken > 0
            ? _questInstanceManager.ResolveQuestReportOwner(client, packet.Pid, questIndex) : 0;
        if (ownerPid <= 0)
        {
            _logger.LogWarning("[Quest] rejected op152 backup PID {0} from client {1}.",
                packet.Pid, client.Id);
            return true;
        }
        _questInstanceManager.SaveGameBackup(instanceToken, ownerPid, packet);
        _logger.LogInformation("[Quest] retained op152 backup for PID {0}, instance {1}, record {2}/{3}, stock {4}.",
            ownerPid, instanceToken, packet.Index + 1, packet.Total, packet.Items.Count);
        return true;
    }
}
