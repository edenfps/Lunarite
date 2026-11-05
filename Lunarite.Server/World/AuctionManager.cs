using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed class AuctionManager
{



    private const int AuctionPageSize = 20;

    internal void SendAuctionPage(
        Client client, byte opcode, IEnumerable<AuctionStore.AuctionItemInfo> source, int direction)
    {
        var all = source.OrderByDescending(x => x.Index).ToList();
        int total = (all.Count + AuctionPageSize - 1) / AuctionPageSize;
        int current = CityManager.ResolvePage(direction, client.Session.AuctionPages.GetValueOrDefault(opcode), total);
        client.Session.AuctionPages[opcode] = current;
        var response = new AuctionList { Opcode = opcode, PageDirection = 1,
            CurrentPage = (byte)current, TotalPages = (byte)Math.Min(byte.MaxValue, total) };
        response.Entries.AddRange(all.Skip(current * AuctionPageSize).Take(AuctionPageSize));
        client.SendPacket(response);
    }
}
