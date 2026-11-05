using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BattleBackendVerifyHandler
{
    private static BattleServerManager _battleServerManager = null!;
    private static BattleZoneCatalog _battleZoneCatalog = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BattleBackendVerifyHandler));
        _battleZoneCatalog = serviceProvider.GetRequiredService<BattleZoneCatalog>();
        _battleServerManager = serviceProvider.GetRequiredService<BattleServerManager>();
    }

    // Loopback, or one of this machine's own addresses (BATTLE_BACKEND_IP set to a LAN address).
    internal static bool IsLocalPeer(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(x => x.GetIPProperties().UnicastAddresses).Any(x => x.Address.Equals(address));
        }
        catch (NetworkInformationException) { return false; }
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out byte version) || !reader.TryRead(out byte battleIndex)
            || !reader.TryRead(out string? address) || !reader.TryRead(out ushort port)
            || reader.RemainingLength != 0) return false;
        // The link has no cipher or credentials (analysis/battle-backend.md): accept only an instance
        // BattleInstanceManager launched on this machine, by its BattleIndex/BattlePort arguments.
        IPAddress remote;
        try { remote = client.IPAddress; }
        catch (ObjectDisposedException) { remote = IPAddress.None; }
        bool trusted = IsLocalPeer(remote) && _battleServerManager.IsLaunched(battleIndex, port);
        if (!trusted)
            _logger.LogWarning("[BattleBackend] refused verify for server {0} port {1} from {2}: not a launched local instance.",
                battleIndex, port, remote);
        byte answer = (byte)(trusted && version == BattleServerManager.BackendVersion && battleIndex != byte.MaxValue
            && !string.IsNullOrWhiteSpace(address) && port != 0 ? 0 : 1);
        if (answer == 0)
        {
            _battleServerManager.SetServer(client.Id, new BattleServerManager.VerifiedServer(battleIndex, port, false, 0));
            _battleServerManager.SetServerClient(client.Id, client);
        }
        client.SendPacket(new BattleBackendVerify { Answer = answer });
        _logger.LogInformation("[BattleBackend] verify server {0} at {1}:{2}: answer {3}.",
            battleIndex, address, port, answer);
        if (answer == 0 && _battleZoneCatalog.GetByServerId(battleIndex) is { } zone)
        {
            client.SendPacket(new BattleBackendStart
            {
                MapIndex = battleIndex,
                MapUrl = zone.LevelName,
                MaxPlayers = (byte)Math.Clamp(zone.MaxPlayers, 1, byte.MaxValue),
                GoalScore = zone.GoalScore,
                TimeLimitMinutes = (byte)Math.Clamp((int)zone.TimeLimitMinutes, 1, byte.MaxValue),
                WarmingUp = 0
            });
            _logger.LogInformation("[BattleBackend] configured server {0}: {1}, max {2}, goal {3}, {4} minutes.",
                battleIndex, zone.LevelName, zone.MaxPlayers, zone.GoalScore, zone.TimeLimitMinutes);
        }
        return true;
    }
}
