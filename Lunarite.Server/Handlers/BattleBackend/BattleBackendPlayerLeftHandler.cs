using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

// Backend op11 `db` (CServerTcpLink vtable +116, sent through sub_111C0880 when a player's
// connection to the battle server closes). A player who quits mid-battle never sends op28, so this
// frees their place, and stops the instance once it is empty.
[PacketHandler]
public static class BattleBackendPlayerLeftHandler
{
    private static BattleServerManager _battleServerManager = null!;
    private static BattleInstanceManager _battleInstanceManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(BattleBackendPlayerLeftHandler));
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
        _battleInstanceManager = serviceProvider.GetRequiredService<BattleInstanceManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int pid) || !reader.TryRead(out byte _) || reader.RemainingLength != 0
            || !_battleServerManager.TryGetServer(client.Id, out var server)) return false;
        _logger.LogInformation("[BattleBackend] PID {0} left server {1}.", pid, server.Index);
        _battleInstanceManager.Leave(pid);
        return true;
    }
}
