using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestPossibleListHandler
{
    private static ILogger _logger = null!;
    private static QuestConditions _questConditions = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestPossibleListHandler));

        _questConditions = serviceProvider.GetRequiredService<QuestConditions>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        _logger.LogDebug("Received {0} packet.", "QuestPossibleList");

        int pid = Game.PidOf(client);
        var questPossibleList = new QuestPossibleList { Quests = _questConditions.Available(pid) };
        _logger.LogInformation("[Quests] PID {0} has {1} possible quest(s): {2}", pid,
            questPossibleList.Quests.Count, string.Join(",", questPossibleList.Quests));

        client.SendPacket(questPossibleList);

        return true;
    }
}
