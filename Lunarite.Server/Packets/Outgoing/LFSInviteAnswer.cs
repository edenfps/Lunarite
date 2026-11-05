using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class LFSInviteAnswer : IPacketSerialize
{
    public byte Answer;
    public int Pid;
    public string PlayerName = "";
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)115); writer.Write(Answer);
        writer.Write(Pid); writer.Write(PlayerName); return writer.Buffer;
    }
}
