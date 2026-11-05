using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op69 OnEquipWeapon (0x11117550), the answer to client op68 [weaponCell, mainCell].
// Parses "b" Result, then "bb" WeaponCell, MainCell on success; the client moves
// MAIN[MainCell] -> WEAPON[WeaponCell] itself and fires script OnEquipWeapon(byResult).
public class EquipWeapon : IPacketSerialize
{
    public byte Result;       // 0 = success
    public byte WeaponCell;   // INVEN_WEAPON destination slot
    public byte MainCell;     // INVEN_MAIN source cell

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)69);
        writer.Write(Result);
        if (Result == 0)
        {
            writer.Write(WeaponCell);
            writer.Write(MainCell);
        }

        return writer.Buffer;
    }
}
