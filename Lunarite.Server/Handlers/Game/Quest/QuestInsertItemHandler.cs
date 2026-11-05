using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;

namespace Lunarite.Handlers;

// ReQuestAddItem is reachable only through HuxleySeqAct_MoveItem's ADD input (the
// HuxleySeqAct_SetQuestPlayerStart caller is commented out), and none of the 1,291 cooked
// quest packages contains a MoveItem action. The request is logged and not granted.
[PacketHandler]
public static class QuestInsertItemHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestInsertItemHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestItemRequest.TryDeserialize(ref reader, out var packet)) return false;
        int pid = Game.PidOf(client);
        _logger.LogWarning("[Quest] op46 unsupported script item grant PID {0}: {1}x{2}.",
            pid, packet.ItemIndex, packet.Quantity);
        return true;
    }
}
