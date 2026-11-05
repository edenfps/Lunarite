using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class PlayerInfoViewHandler
{
    private static CharacterManager _characterManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerInfoViewHandler));
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!PlayerInfoViewRequest.TryDeserialize(ref reader, out var packet)) return false;
        int pid = packet.Pid;
        var profile = _characterManager.BuildAnotherPlayerInfo(pid);
        if (profile == null)
        {
            _logger.LogWarning("[PlayerInfo] requested unknown PID {0}.", pid);
            client.SendPacket(new ReceivedAnotherPlayerInfo { Result = 1 });
            return true;
        }
        client.SendPacket(profile);
        return true;
    }
}
