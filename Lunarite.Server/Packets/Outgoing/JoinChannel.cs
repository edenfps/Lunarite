using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// ProcSimulRoomList (server op130) accepts result 0 followed by list type
// 0xFE/0xFF without a room body. 0xFE is the retail initial empty-list marker.
public sealed class JoinChannel : IPacketSerialize
{
    public byte Result;
    public byte ListType = 0xFE;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)130);
        writer.Write(Result);
        if (Result == 0)
            writer.Write(ListType);
        return writer.Buffer;
    }
}
