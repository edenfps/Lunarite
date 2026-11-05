using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestAcquireListHandler
{
    private static QuestManager _questManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestAcquireListHandler));
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        _logger.LogDebug("Received {0} packet.", "QuestAcquireList");

        int pid = Game.PidOf(client);
        _questManager.SendQuestAcquireList(client, pid);

        return true;
    }
}
