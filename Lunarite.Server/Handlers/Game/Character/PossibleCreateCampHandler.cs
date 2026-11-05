using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;

namespace Lunarite.Handlers;

[PacketHandler]
public static class PossibleCreateCampHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PossibleCreateCampHandler));
    }

    // The create-player menu interprets 0 as both factions, 1 as Sapiens and 2 as Alternatives.
    // Retail locked an account to the faction of its first character; this server always offers
    // both (CharacterStore.TryCreate accepts either).
    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        _logger.LogDebug("Received {0} packet.", "PossibleCreateCamp");

        client.SendPacket(new PossibleCreateCamp { PossibleCamp = 0 });

        return true;
    }
}
