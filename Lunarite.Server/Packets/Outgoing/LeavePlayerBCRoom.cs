using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class LeavePlayerBCRoom : IPacketSerialize
{
    public int Pid;
    public byte RoomId;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)133); w.Write(Pid); w.Write(RoomId);
        return w.Buffer;
    }
}
