using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ClanAppointmentInfo : IPacketSerialize
{
    public byte Result;
    public byte SubMaster;
    public byte SquadMaster;
    public byte SubSquadMaster;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)189); writer.Write(Result);
        if (Result == 37) { writer.Write(SubMaster); writer.Write(SquadMaster); writer.Write(SubSquadMaster); }
        return writer.Buffer;
    }
}
