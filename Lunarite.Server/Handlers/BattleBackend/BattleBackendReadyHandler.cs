using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleBackendReadyHandler
{
    private static BattleServerManager _battleServerManager = null!;
    private static BattleFront _battleFront = null!;
    private static BattleZoneCatalog _battleZoneCatalog = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {

        _battleFront = serviceProvider.GetRequiredService<BattleFront>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BattleBackendReadyHandler));
        _battleZoneCatalog = serviceProvider.GetRequiredService<BattleZoneCatalog>();
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out ushort maxPlayers) || reader.RemainingLength != 0
            || !_battleServerManager.TryGetServer(client.Id, out var server)) return false;
        var zone = _battleZoneCatalog.GetByServerId(server.Index);
        if (maxPlayers == 0 || zone == null || maxPlayers != zone.MaxPlayers)
        {
            _logger.LogWarning("[BattleBackend] server {0} reported invalid capacity {1}.", server.Index, maxPlayers);
            return true;
        }
        _battleServerManager.SetServer(client.Id, server with { Ready = true });
        _logger.LogInformation("[BattleBackend] server {0} is match-ready for {1} players.", server.Index, maxPlayers);
        // The match's game exists now; tell it which species holds a war-front battleground.
        if (zone.OnFront)
        {
            byte species = _battleFront.OccupantOf(zone.EntryIndex);
            client.SendPacket(new BattleBackendOccupation { Species = species });
            _logger.LogInformation("[BattleBackend] server {0} defended by species {1}.", server.Index, species);
        }
        return true;
    }
}
