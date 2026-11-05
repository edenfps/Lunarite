using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native ProcClanNotice (op182): result 19 carries the notice, posted to clan chat.
public sealed class ClanNotice : IPacketSerialize
{
    public byte Result;
    public string Notice = "";
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)182); writer.Write(Result);
        if (Result == 19) writer.Write(Notice);
        return writer.Buffer;
    }
}
