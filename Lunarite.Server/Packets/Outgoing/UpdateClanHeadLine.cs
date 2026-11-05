using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op215 / OnUpdateClanHeadLine: PID, clan name, clan rank.
public sealed class UpdateClanHeadLine : IPacketSerialize
{
    public int Pid;
    public string ClanName = "";
    public byte Rank;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)215); writer.Write(Pid); writer.Write(ClanName); writer.Write(Rank);
        return writer.Buffer;
    }
}
