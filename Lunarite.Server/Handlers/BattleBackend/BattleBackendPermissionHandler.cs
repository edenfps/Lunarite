using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleBackendPermissionHandler
{
    private static BattleServerManager _battleServerManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BattleBackendPermissionHandler));
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int pid) || !reader.TryRead(out byte answer)
            || reader.RemainingLength != 0 || !_battleServerManager.TryGetServer(client.Id, out var server)) return false;
        if (_battleServerManager.TryGetPendingPermission((server.Index, pid), out var pending))
        {
            pending.Answer = answer;
            pending.Signal.Set();
        }
        _logger.LogInformation("[BattleBackend] permission PID {0} on server {1}: answer {2}.", pid, server.Index, answer);
        return true;
    }
}
