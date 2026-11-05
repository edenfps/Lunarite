using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op25 OnChat: bsss = type, from, to, message.
public sealed class Chat : IPacketSerialize
{
    public byte Type;
    public string From = "";
    public string To = "";
    public string Message = "";

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)25); writer.Write(Type); writer.Write(From); writer.Write(To); writer.Write(Message);
        return writer.Buffer;
    }
}
