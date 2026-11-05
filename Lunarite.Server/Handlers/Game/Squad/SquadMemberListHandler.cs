using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SquadMemberListHandler
{
    private static SquadManager _squadManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(SquadMemberListHandler));
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0)
            return false;

        _logger.LogDebug("Received {0} packet.", "SquadMemberList");
        _squadManager.SendSquadStateAndMembers(client);
        return true;
    }
}
