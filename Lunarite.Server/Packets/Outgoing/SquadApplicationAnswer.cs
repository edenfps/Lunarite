using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op105 OnSquadApplicationAnswer parses `bss`.
public sealed class SquadApplicationAnswer : IPacketSerialize
{
    public byte Answer;
    public string LeaderName = "";
    public string SquadName = "";

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)105); writer.Write(Answer); writer.Write(LeaderName); writer.Write(SquadName);
        return writer.Buffer;
    }
}
