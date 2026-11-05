using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransmitContentHandler
{
    private static ILogger _logger = null!;
    private static TransmitStore _transmitStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(TransmitContentHandler));

        _transmitStore = serviceProvider.GetRequiredService<TransmitStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!TransmitContentRequest.TryDeserialize(ref reader, out var packet))
            return false;

        int pid = Game.PidOf(client);
        var message = _transmitStore.Get(pid, packet.MessageSequence);
        if (message == null)
        {
            _logger.LogWarning("Transmit content {0} not found for PID {1}.", packet.MessageSequence, pid);
            return true; // op201 has no failure discriminator
        }
        client.SendPacket(new TransmitContent
        {
            MessageSequence = message.Sequence,
            Type = message.Type,
            From = message.From,
            SenderPid = message.SenderPid,
            Title = message.Title,
            Content = message.Content,
            CR = message.CR,
            Attachments = message.Attachments
                .Where(x => !x.Claimed)
                .Select(x => new TransmitContent.Attachment(x.Sequence, x.Item.ItemId, x.Item.Count))
                .ToList()
        });
        return true;
    }
}
