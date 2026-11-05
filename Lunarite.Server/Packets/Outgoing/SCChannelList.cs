using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native ProcSimulChannelList (server op129) reads a byte count followed by
// count raw, 8-byte SCChannelInfo structures. The fourth byte is the native
// alignment pad before nPlayerCount.
public sealed class SCChannelList : IPacketSerialize
{
    public List<SimulationChannel> Channels { get; } = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)129);
        writer.Write((byte)Channels.Count);
        foreach (var channel in Channels)
        {
            writer.Write(channel.Region);
            writer.Write(channel.Group);
            writer.Write(channel.Id);
            writer.Write((byte)0);
            writer.Write(channel.PlayerCount);
        }
        return writer.Buffer;
    }
}
