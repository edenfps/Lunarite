using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class AuctionCheckTimeout : IPacketSerialize
{
    public bool HasItems;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write((byte)170); writer.Write((byte)(HasItems ? 1 : 0));
        return writer.Buffer;
    }
}
