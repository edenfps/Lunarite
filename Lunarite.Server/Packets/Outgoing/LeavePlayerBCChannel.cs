using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class LeavePlayerBCChannel : IPacketSerialize
{
    public byte RoomId;
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)148); w.Write(RoomId); return w.Buffer;
    }
}
