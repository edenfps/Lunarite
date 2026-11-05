using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op222 consumes one null-terminated string, formats it through the
// localized HuxleyPCMMOHUD.ListenerMsg template, and displays it as a HUD notice.
public sealed class ListenerNotice : IPacketSerialize
{
    public string Message = "";

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)222);
        writer.Write(Message);
        return writer.Buffer;
    }
}
