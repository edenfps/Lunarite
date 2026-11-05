using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ErrorLogHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ErrorLogHandler));
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // The script declaration exposes two character IDs and six value arguments,
        // but the June native sender emits only `bdddddd` (type plus six dwords).
        if (!reader.TryRead(out byte errorType)) return false;
        int[] values = new int[6];
        for (int i = 0; i < values.Length; i++)
            if (!reader.TryRead(out values[i])) return false;
        if (reader.RemainingLength != 0) return false;
        _logger.LogWarning("[ClientError] PID {0}, type {1}, data [{2}].",
            Game.PidOf(client), errorType, string.Join(",", values));
        return true;
    }
}
