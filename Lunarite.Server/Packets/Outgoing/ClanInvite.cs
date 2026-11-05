using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ClanInvite : IPacketSerialize
{
    public byte Result;
    public string MasterName = "";
    public string ClanName = "";
    // ProcClanInvite prints "<name> ..." for 9 (declined), 50 (already in a clan), 52 (faction).
    public string PlayerName = "";
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)179); writer.Write(Result);
        if (Result == 7) { writer.Write(MasterName); writer.Write(ClanName); }
        else if (Result is 9 or 50 or 52) writer.Write(PlayerName);
        return writer.Buffer;
    }
}
