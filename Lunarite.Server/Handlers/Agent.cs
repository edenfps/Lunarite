using System;
using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;

namespace Lunarite.Handlers;

[Handler(45405, HandlerType.Agent)]
public static class Agent
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Agent));
    }

    // The listener binds all interfaces, so advertise the one that accepted this connection;
    // 127.0.0.1 would send remote clients to themselves. Behind NAT, configure the public IPv4.
    public static IPAddress ResolveGameAddress(string? configuredAddress, IPAddress localAddress,
        string settingName = "HUXLEY_GAME_PUBLIC_IP")
    {
        if (!string.IsNullOrWhiteSpace(configuredAddress))
        {
            if (!IPAddress.TryParse(configuredAddress.Trim(), out var configured)
                || configured.AddressFamily != AddressFamily.InterNetwork
                || configured.GetAddressBytes()[0] is 0 or >= 224)
                throw new InvalidOperationException($"{settingName} must be a usable IPv4 address.");
            return configured;
        }
        if (localAddress.IsIPv4MappedToIPv6) localAddress = localAddress.MapToIPv4();
        return localAddress.AddressFamily == AddressFamily.InterNetwork
            && !localAddress.Equals(IPAddress.Any) ? localAddress : IPAddress.Loopback;
    }

    internal static IPAddress GameAddressFor(Client client) => ResolveGameAddress(
        Environment.GetEnvironmentVariable("HUXLEY_GAME_PUBLIC_IP"), client.LocalIPAddress);

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
            0 => AgentConnectHandler.HandlePacket(client, ref reader),
            6 => NewMemberHandler.HandlePacket(client, ref reader),
            7 => LoginHandler.HandlePacket(client, ref reader),
            8 => SelectServerHandler.HandlePacket(client, ref reader),
            _ => false
        };

        if (!handled)
            _logger.LogWarning("Unhandled packet received. ( Type: {0}, Data: {1} )", type, Convert.ToHexString(data));
    }

    // Server-select entry name (HUXLEY_SERVER_NAME); the list carries this one game server.
    public static string ServerName =>
        Environment.GetEnvironmentVariable("HUXLEY_SERVER_NAME") is { Length: > 0 } name ? name : "Huxley";

    // Players for 100% load (HUXLEY_SERVER_CAPACITY, default 200, about what the city stress test
    // keeps smooth). The client labels 0-39% <Normal>, 40-89% <Crowded>, 90%+ <Congested>.
    public static int ServerCapacity =>
        int.TryParse(Environment.GetEnvironmentVariable("HUXLEY_SERVER_CAPACITY"), out int capacity)
        && capacity > 0 ? capacity : 200;

    public static byte LoadPercent(int online, int capacity) =>
        (byte)Math.Clamp(online * 100L / Math.Max(1, capacity), 0, 100);

    // At capacity, server select answers SELECTSERVER_FULLPLAYER (4, "Server population is currently at its
    // maximum capacity."); GM accounts still get in.
    public static bool IsFull(int online, int capacity, bool gm) => !gm && online >= Math.Max(1, capacity);
}
