using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

// Backend op10 `db` (CServerTcpLink vtable +112, sent through sub_111C07E0 when UWorld logs
// "Join succeeded"): a registered player finished joining the battle server.
[PacketHandler]
public static class BattleBackendPlayerJoinedHandler
{
    private static BattleServerManager _battleServerManager = null!;
    private static BattleInstanceManager _battleInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(BattleBackendPlayerJoinedHandler));
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
        _battleInstanceManager = serviceProvider.GetRequiredService<BattleInstanceManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int pid) || !reader.TryRead(out byte state) || reader.RemainingLength != 0
            || !_battleServerManager.TryGetServer(client.Id, out var server)) return false;
        _logger.LogInformation("[BattleBackend] PID {0} joined server {1} (state {2}).", pid, server.Index, state);
        _battleInstanceManager.MarkJoined(pid);
        return true;
    }
}
