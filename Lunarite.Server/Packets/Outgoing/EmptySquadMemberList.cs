using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class EmptySquadMemberList : IPacketSerialize
{
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)100); writer.Write((byte)0);
        return writer.Buffer;
    }
}
