using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnMoveEvent: db.
public sealed class MoveEvent : IPacketSerialize
{
    public int Pid;
    public byte Event;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)24); writer.Write(Pid); writer.Write(Event);
        return writer.Buffer;
    }
}
