using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class LFSInvite : IPacketSerialize
{
    public byte Answer;
    public int SquadKey;
    public string SquadName = "";
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)114); writer.Write(Answer);
        if (Answer == 0) { writer.Write(SquadKey); writer.Write(SquadName); }
        return writer.Buffer;
    }
}
