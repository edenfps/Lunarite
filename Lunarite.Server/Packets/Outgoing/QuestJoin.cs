using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// b
// dm (when Answer==0)
// Client OnQuestJoin @0x1110ff00: a non-zero answer goes to script OnQuestJoin as an error.
// On 0 it reads the packed server IP and 16-byte connection blob (kept in globals);
// sub_1119D180 formats the IP with %i.%i.%i.%i (sub_1093FF40) and runs "START <ip>?seamless".
public class QuestJoin : IPacketSerialize
{
    public byte Answer;
    public uint ServerIP;
    public byte[] Data = new byte[16];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)52);

        writer.Write(Answer);

        if (Answer == 0)
        {
            writer.Write(ServerIP);
            writer.Write(Data);
        }

        return writer.Buffer;
    }
}
