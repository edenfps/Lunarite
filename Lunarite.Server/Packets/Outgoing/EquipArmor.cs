using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server opcode 67 -> native OnEquipArmor. Success body is `bbb`:
// result, equipped armor-part cell, source MAIN cell.
public sealed class EquipArmor : IPacketSerialize
{
    public byte Result;
    public byte ArmorCell;
    public byte MainCell;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)67);
        writer.Write(Result);
        if (Result == 0)
        {
            writer.Write(ArmorCell);
            writer.Write(MainCell);
        }
        return writer.Buffer;
    }
}
