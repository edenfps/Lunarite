using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class UpdateItemTuneUp : IPacketSerialize
{
    public byte Inventory, Cell;
    public int TuneUpOptions;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)213); w.Write(Inventory); w.Write(Cell); w.Write(TuneUpOptions);
        return w.Buffer;
    }
}
