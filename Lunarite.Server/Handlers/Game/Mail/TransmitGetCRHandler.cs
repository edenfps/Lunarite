using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransmitGetCRHandler
{
    private static CharacterManager _characterManager = null!;
    private static TransmitManager _transmitManager = null!;
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(TransmitGetCRHandler));
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _transmitManager = serviceProvider.GetRequiredService<TransmitManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!TransmitGetCRRequest.TryDeserialize(ref reader, out var packet))
            return false;

        int pid = Game.PidOf(client);
        var (answer, amount) = _transmitManager.TransmitGetCR(pid, packet.MessageSequence);
        client.SendPacket(new TransmitGetCR
        {
            Answer = answer,
            MessageSequence = packet.MessageSequence
        });
        if (answer == TransmitResultCode.Ok)
        {
            _characterManager.SendMoneyParameter(client, pid);
            _logger.LogInformation("[Transmit] PID {0} claimed {1} CR from message {2}.", pid, amount, packet.MessageSequence);
        }
        return true;
    }
}
