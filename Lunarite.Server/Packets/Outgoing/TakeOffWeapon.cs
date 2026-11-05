using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op72: result, then equipped slot, resolved MAIN destination, and optional MAIN relocation.
public sealed class TakeOffWeapon : IPacketSerialize
{
    public byte Result;
    public byte WeaponCell;
    public byte MainCell;
    public byte RelocatedMainCell = 0xFF;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)72);
        writer.Write(Result);
        if (Result == 0)
        {
            writer.Write(WeaponCell);
            writer.Write(MainCell);
            writer.Write(RelocatedMainCell);
        }
        return writer.Buffer;
    }
}
