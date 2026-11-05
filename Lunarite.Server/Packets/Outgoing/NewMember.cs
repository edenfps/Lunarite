using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op6 OnNewMember parses one answer byte; zero is success.
public sealed class NewMember : IPacketSerialize
{
    public byte Answer;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)6); writer.Write(Answer);
        return writer.Buffer;
    }
}
