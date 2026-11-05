using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native ProcPriceRate reads eight consecutive floats: buy/sell for both factions,
// repair for both factions, then tune-up for both factions.
public sealed class PriceRate : IPacketSerialize
{
    public float SapiBuy = 1;
    public float SapiSell = 1;
    public float AlterBuy = 1;
    public float AlterSell = 1;
    public float SapiRepair = 1;
    public float AlterRepair = 1;
    public float SapiTuneUp = 1;
    public float AlterTuneUp = 1;

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)27);
        w.Write(SapiBuy); w.Write(SapiSell); w.Write(AlterBuy); w.Write(AlterSell);
        w.Write(SapiRepair); w.Write(AlterRepair); w.Write(SapiTuneUp); w.Write(AlterTuneUp);
        return w.Buffer;
    }
}
