using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransmitListHandler
{
    private static ILogger _logger = null!;
    private static TransmitStore _transmitStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(TransmitListHandler));

        _transmitStore = serviceProvider.GetRequiredService<TransmitStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!TransmitListRequest.TryDeserialize(ref reader, out var packet))
            return false;

        int pid = Game.PidOf(client);
        var all = _transmitStore.List(pid);
        int totalPages = TransmitManager.TransmitPageCount(all.Count);
        int page = CityManager.ResolvePage(packet.PageDirection, client.Session.TransmitPage, totalPages);
        client.Session.TransmitPage = page;

        var response = new TransmitList
        {
            // The native op199 parser only reads records when the response is PAGE_CUR (1);
            // any other direction is treated as header-only.
            PageDirection = 1,
            CurrentPage = (byte)page,
            TotalPages = (byte)totalPages
        };
        foreach (var message in all.Skip(page * TransmitManager.TransmitPageSize).Take(TransmitManager.TransmitPageSize))
        {
            response.Entries.Add(new TransmitList.Entry
            {
                Sequence = message.Sequence,
                Type = message.Type,
                From = message.From,
                Title = message.Title,
                Read = message.Read ? (byte)1 : (byte)0
            });
        }
        _logger.LogInformation("Mailbox list PID {0}: direction={1}, page={2}/{3}, entries={4}, total={5}.",
            pid, packet.PageDirection, page, totalPages, response.Entries.Count, all.Count);
        client.SendPacket(response);
        return true;
    }
}
