using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native sub_111137C0/op230 and sub_11113880/op232 update the already-loaded
// PlayerInfo arrays against the server Unix time.
public sealed class UpdateListOfDurativeItem : IPacketSerialize
{
    public int UnixSeconds;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)230);
        writer.Write(UnixSeconds);
        return writer.Buffer;
    }
}
