using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

namespace Lunarite.Handlers;

[Handler(55902, HandlerType.Battle)]
public static class BattleBackend
{
    private static ILogger _logger = null!;
    private static BattleServerManager _battleServerManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(BattleBackend));
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
    }

    public static void OnHandlePacket(Client client, ReadOnlySpan<byte> data)
    {
        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte type))
        {
            _logger.LogError("Failed to read packet type. ( Data: {0} )", Convert.ToHexString(data));
            return;
        }

        var handled = type switch
        {
            0 => BattleBackendMatchResultHandler.HandlePacket(client, ref reader),
            3 => BattleBackendResultHandler.HandlePacket(client, ref reader),
            4 => BattleBackendUseItemHandler.HandlePacket(client, ref reader),
            6 => BattleBackendVerifyHandler.HandlePacket(client, ref reader),
            8 => BattleBackendReadyHandler.HandlePacket(client, ref reader),
            9 => BattleBackendPermissionHandler.HandlePacket(client, ref reader),
            10 => BattleBackendPlayerJoinedHandler.HandlePacket(client, ref reader),
            11 => BattleBackendPlayerLeftHandler.HandlePacket(client, ref reader),
            12 => BattleBackendTimeHandler.HandlePacket(client, ref reader),
            _ => false
        };

        if (!handled)
            _logger.LogWarning("Unhandled packet received. ( Type: {0}, Data: {1} )", type, Convert.ToHexString(data));
    }

    public static void OnDisconnect(Client client)
    {
        _battleServerManager.RemoveLink(client.Id);
    }
}
