using System.Net;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// b
// dw
public class SelectServer : IPacketSerialize
{
    public byte Answer;

    public required IPAddress IP;
    public ushort Port;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)9);

        writer.Write(Answer);

        if (Answer == 0)
        {
            writer.Write(IP.GetAddressBytes());

            writer.Write(Port);
        }

        return writer.Buffer;
    }
}