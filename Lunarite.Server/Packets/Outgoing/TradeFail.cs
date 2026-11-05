using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// op85 TradeAction.Fail.
public sealed class TradeFail : IPacketSerialize
{
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)85); writer.Write(TradeAction.Fail);
        return writer.Buffer;
    }
}
