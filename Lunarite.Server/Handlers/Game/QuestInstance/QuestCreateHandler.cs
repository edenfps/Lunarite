using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestCreateHandler
{
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestCreateHandler));
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestCheckRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", "QuestCreate");
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( Index={1} Difficulty={2} )", "QuestCreate", packet.Index, packet.DifficultyLevel);
        return _questInstanceManager.QuestCreate(client, packet.Index, packet.DifficultyLevel);
    }
}
