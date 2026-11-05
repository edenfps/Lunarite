using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// b
// db
public class Login : IPacketSerialize
{
    public byte Answer;

    public int UserId;
    public bool Teenager;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)7);

        writer.Write(Answer);

        if (Answer == 0)
        {
            writer.Write(UserId);

            writer.Write(Teenager);
        }

        return writer.Buffer;
    }
}
