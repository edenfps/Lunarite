using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class NATTest : IPacketSerialize
{
    public List<P2PEndpoint> Endpoints { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)127);
        int count = Math.Min(byte.MaxValue, Endpoints.Count);
        writer.Write((byte)count);
        foreach (var endpoint in Endpoints.Take(count))
        {
            writer.Write(endpoint.Address);
            writer.Write(endpoint.Port);
        }
        return writer.Buffer;
    }
}
