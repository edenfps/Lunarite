using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.World;

namespace Lunarite.Handlers;

// "Return to City" at the quest-end node. The client has already run PreLeaveQuest (hides
// the pawn 1,000,000 units below the map), so without travel the pawn dies and respawns in
// the instance. -1/-1 means no explicit return node.
[PacketHandler]
public static class QuestLeaveHandler
{
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestLeaveHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestLeaveRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(QuestLeaveRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet ( {1} ).", "QuestLeave", packet);

        _questInstanceManager.QuestLeave(client, packet.ZoneIndex, packet.AreaIndex, "left");
        return true;
    }
}
