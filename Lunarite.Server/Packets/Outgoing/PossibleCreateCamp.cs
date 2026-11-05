using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// b
public class PossibleCreateCamp : IPacketSerialize
{
    public byte PossibleCamp;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)18);

        writer.Write(PossibleCamp);

        return writer.Buffer;
    }
}