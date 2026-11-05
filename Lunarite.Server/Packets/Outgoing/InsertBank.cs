using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class InsertBank : IPacketSerialize
{
    public byte Result;
    public byte MainCell;
    public byte BankCell;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)86); w.Write(Result);
        if (Result == 0) { w.Write(MainCell); w.Write(BankCell); }
        return w.Buffer;
    }
}
