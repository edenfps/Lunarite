using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Handlers;

// Backend op0 `bb`, sent by a dedicated server's SendBattleResult (0x111CBE10) before the
// players' op3 results: GRI.WinSpecies (ESpeciesType, 0 for a draw) and GRI.DominatingVictory.
// The winning species decides a war-front battleground even when no winner reports a result.
[PacketHandler]
public static class BattleBackendMatchResultHandler
{
    private static BattleServerManager _battleServerManager = null!;
    private static BattleZoneCatalog _battleZoneCatalog = null!;
    private static BattleFront _battleFront = null!;
    private static BattleManager _battleManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(BattleBackendMatchResultHandler));
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
        _battleZoneCatalog = serviceProvider.GetRequiredService<BattleZoneCatalog>();
        _battleFront = serviceProvider.GetRequiredService<BattleFront>();
        _battleManager = serviceProvider.GetRequiredService<BattleManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte winSpecies) || !reader.TryRead(out byte dominatingVictory)
            || reader.RemainingLength != 0 || !_battleServerManager.TryGetServer(client.Id, out var server))
            return false;
        _logger.LogInformation("[BattleBackend] server {0} match ended: species {1} won{2}.",
            server.Index, winSpecies, dominatingVictory != 0 ? " (dominating victory)" : "");
        _battleServerManager.SetDominatingVictory(client.Id, dominatingVictory != 0);
        if (_battleZoneCatalog.GetByServerId(server.Index) is { OnFront: true } zone
            && _battleFront.RecordResult(zone.EntryIndex, winSpecies) is { } outcome)
        {
            _battleManager.PublishBattleFrontOutcome(outcome);
            client.SendPacket(new BattleBackendOccupation { Species = outcome.ToSpecies });
        }
        return true;
    }
}
