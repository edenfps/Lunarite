using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class TakeoutBank : IPacketSerialize
{
    public byte Result;
    public byte BankCell;
    public byte MainCell;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)87); w.Write(Result);
        if (Result == 0) { w.Write(BankCell); w.Write(MainCell); }
        return w.Buffer;
    }
}
