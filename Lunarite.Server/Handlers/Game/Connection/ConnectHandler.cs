using System;
using System.Linq;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class ConnectHandler
{
    private static CharacterManager _characterManager = null!;
    private static SimulationManager _simulationManager = null!;
    private static ILogger _logger = null!;
    private static AccountSessionStore _accountSessionStore = null!;
    private static PlayerManager _playerManager = null!;
    private static P2PManager _p2pManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ConnectHandler));

        _accountSessionStore = serviceProvider.GetRequiredService<AccountSessionStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _simulationManager = serviceProvider.GetRequiredService<SimulationManager>();
        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _p2pManager = serviceProvider.GetRequiredService<P2PManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!ConnectRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(ConnectRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(ConnectRequest), packet);

        // The retail client sends Connect once per Game socket (OnGameVersion).
        if (client.Session.AccountId != null)
        {
            _logger.LogWarning("[Accounts] Game connection {0} sent Connect again; closing it.", client.Id);
            client.Dispose();
            return true;
        }
        // One try per Game socket: a refused Connect (no ticket, two accounts' tickets on one
        // address, or the address over its Connect budget) closes the link.
        if (!_accountSessionStore.TryClaim(client.IPAddress, out int userId, packet.Address,
                Environment.GetEnvironmentVariable("HUXLEY_TRUST_NAT_LOCAL_IP") == "1"))
        {
            _logger.LogWarning("[Accounts] Game connection {0} has no unambiguous Agent handoff ticket, or its address is over the Connect budget.", client.Id);
            client.Dispose();
            return true;
        }
        // One Game link per account, as LOGIN_SAMEUSER enforces at the Agent. City travel and a
        // quest return reopen the Game socket, and the old one may not have closed yet (or be
        // half-open), so the fresh handoff wins and the older link is closed.
        foreach (var older in _playerManager.Links.Where(link => link != client && link.Session.AccountId == userId).ToList())
        {
            _logger.LogWarning("[Accounts] Game connection {0} replaces user {1}'s older Game link {2}.",
                client.Id, userId, older.Id);
            _playerManager.RemoveLink(older);
            older.Dispose();
        }
        client.Session.AccountId = userId;
        client.Session.NatEndpoint = null;
        client.Session.NetworkAddresses = null;
        client.Session.HolePunchAcknowledged = false;
        client.Session.LocalAddress = packet.Address;
        _playerManager.AddLink(client);
        // The June client binds its P2P socket before opening this Game connection, counting up
        // from 7777 on collision, so ports are allocated in connection order, not room-entry order.
        _simulationManager.AssignPort(client);

        client.SendPacket(new ServerTime
        {
            TickMilliseconds = Environment.TickCount & int.MaxValue,
            UnixSeconds = checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        });
        client.SendPacket(new ServerVersion { Version = 281 });
        _characterManager.SendPlayerList(client);
        if (_p2pManager.NatTestService is { } natTest)
        {
            client.SendPacket(natTest.BuildAnnouncement(P2PManager.NatTestAddress(client)));
        }

        return true;
    }
}
