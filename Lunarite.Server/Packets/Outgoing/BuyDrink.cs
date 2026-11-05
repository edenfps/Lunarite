using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class BuyDrink : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)237);
        writer.Write(Result);
        return writer.Buffer;
    }
}
