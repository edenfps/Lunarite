using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnServerTime decodes opcode 2 as `dd`: monotonic server tick milliseconds,
// followed by Unix-style wall-clock seconds used by item expiration displays.
public sealed class ServerTime : IPacketSerialize
{
    public int TickMilliseconds;
    public int UnixSeconds;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)2);
        writer.Write(TickMilliseconds);
        writer.Write(UnixSeconds);
        return writer.Buffer;
    }
}
