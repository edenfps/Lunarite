using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class MoveInputHandler
{
    private static CityManager _cityManager = null!;
    private static SquadManager _squadManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MoveInputHandler));
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!MoveInputRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(MoveInputRequest));
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( {1} )", nameof(MoveInputRequest), packet);

        int pid = Game.PidOf(client);
        if (pid > 0)
        {
            if (!float.IsFinite(packet.X) || !float.IsFinite(packet.Y) || !float.IsFinite(packet.Z))
            {
                var correction = (Game.PlayerFor(pid).Position ?? default);
                client.SendPacket(new MoveInputForce
                {
                    X = correction.X,
                    Y = correction.Y,
                    Z = correction.Z,
                    Yaw = correction.Yaw
                });
                _logger.LogWarning("[Movement] rejected non-finite position from PID {0}.", pid);
                return true;
            }
            Game.PlayerFor(pid).Position = (packet.X, packet.Y, packet.Z, packet.Yaw);
            if (Game.PlayerFor(pid).IsSpawned)
                _cityManager.BroadcastPresence(client, new MoveInput
                {
                    Pid = pid, X = packet.X, Y = packet.Y, Z = packet.Z, Yaw = packet.Yaw
                });
            _squadManager.BroadcastSquad(pid, (leaderPid, target) => target.SendPacket(new SquadMoveInput
            {
                Pid = pid,
                IsLeader = leaderPid == pid ? (byte)1 : (byte)0,
                X = packet.X,
                Y = packet.Y
            }));
        }

        return true;
    }
}
