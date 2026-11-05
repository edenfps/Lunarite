using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op77. Result 0 carries inventory/source/target and performs a native local swap.
// Result 31 carries no cells and is treated as success after op81/op82 refreshes.
public sealed class SwapItemInSameInven : IPacketSerialize
{
    public byte Result;
    public byte Inventory;
    public byte SourceCell;
    public byte TargetCell;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)77);
        writer.Write(Result);
        if (Result == 0)
        {
            writer.Write(Inventory);
            writer.Write(SourceCell);
            writer.Write(TargetCell);
        }
        return writer.Buffer;
    }
}
