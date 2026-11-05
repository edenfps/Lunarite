using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestCompletionListHandler
{
    private static ILogger _logger = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestCompletionListHandler));

        _questStore = serviceProvider.GetRequiredService<QuestStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        _logger.LogDebug("Received {0} packet.", "QuestCompletionList");

        int pid = Game.PidOf(client);
        var questCompletionList = new QuestCompletionList
        {
            Quests = _questStore.Completed(pid).Order().ToList()
        };

        client.SendPacket(questCompletionList);

        return true;
    }
}
