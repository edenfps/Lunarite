using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OwnerPingTime (server op224) reads a byte count followed by repeated
// `dd` PID/ping pairs and installs them into the active P2P socket.
public sealed class OwnerPingTime : IPacketSerialize
{
    public List<SimulationPing> Entries { get; } = [];

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)224); w.Write((byte)Math.Min(byte.MaxValue, Entries.Count));
        foreach (var entry in Entries.Take(byte.MaxValue))
        {
            w.Write(entry.Pid); w.Write(entry.Milliseconds);
        }
        return w.Buffer;
    }
}
