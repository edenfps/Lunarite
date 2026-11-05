using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ClanInfo : IPacketSerialize
{
    public byte Result;
    public string ClanName = "";
    public string MasterName = "";
    public string Notice = "";
    public int Mark;
    public int Contribution;
    public byte Level = 1;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)180); writer.Write(Result);
        if (Result == 16)
        {
            // ProcClanInfo reads name, notice, then master.
            writer.Write(ClanName); writer.Write(Notice); writer.Write(MasterName);
            writer.Write(Mark); writer.Write(Contribution); writer.Write(Level);
        }
        return writer.Buffer;
    }
}
