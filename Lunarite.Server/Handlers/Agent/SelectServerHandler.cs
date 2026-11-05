using System;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SelectServerHandler
{
    private static ILogger _logger = null!;
    private static AccountSessionStore _accountSessionStore = null!;
    private static World.PlayerManager _playerManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(SelectServerHandler));

        _accountSessionStore = serviceProvider.GetRequiredService<AccountSessionStore>();
        _playerManager = serviceProvider.GetRequiredService<World.PlayerManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!Packets.Incoming.SelectServerRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(Packets.Incoming.SelectServerRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(Packets.Incoming.SelectServerRequest), packet);

        if (client.Session.AccountId is not int userId)
        {
            _logger.LogWarning("[Accounts] rejected server selection without authenticated Agent session.");
            client.SendPacket(new Packets.Outgoing.SelectServer
            {
                Answer = 1,
                IP = IPAddress.Loopback
            });
            return true;
        }
        int online = _playerManager.OnlinePids.Count;
        if (Agent.IsFull(online, Agent.ServerCapacity, client.Session.IsGm))
        {
            _logger.LogInformation("[Accounts] server full ({0}/{1}); refused account {2}.", online, Agent.ServerCapacity, userId);
            client.SendPacket(new Packets.Outgoing.SelectServer { Answer = 4, IP = IPAddress.Loopback });
            return true;
        }
        _accountSessionStore.Issue(client.IPAddress, userId, client.Session.LocalAddress,
            Environment.GetEnvironmentVariable("HUXLEY_TRUST_NAT_LOCAL_IP") == "1");

        var selectServer = new Packets.Outgoing.SelectServer
        {
            Answer = 0,
            IP = Agent.GameAddressFor(client),
            Port = 45406
        };

        client.SendPacket(selectServer);

        return true;
    }
}
