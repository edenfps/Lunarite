using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native ProcNotifyExpirationItem (`ddbb`). AfterSelectPlayer asks the client to immediately
// drain its queued ExpirationItem records through OnNotifyExpirationItem.
public sealed class NotifyExpirationItem : IPacketSerialize
{
    public int ItemId;
    public int TuneUpOptions;
    public byte Type;
    public bool AfterSelectPlayer;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)219); writer.Write(ItemId); writer.Write(TuneUpOptions);
        writer.Write(Type); writer.Write(AfterSelectPlayer);
        return writer.Buffer;
    }
}
