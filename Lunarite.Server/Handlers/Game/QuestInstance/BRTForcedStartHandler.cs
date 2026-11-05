using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BRTForcedStartHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BRTForcedStartHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Developer/test command in the retail scripts; BRT rooms are not implemented.
        if (reader.RemainingLength != 0) return false;
        _logger.LogWarning("[BRT] ignored forced-start from PID {0}; no BRT room exists.",
            Game.PidOf(client));
        return true;
    }
}
