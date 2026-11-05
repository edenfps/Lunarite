using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.Packets.Incoming;

// UHuxleySubsystem::SendQuestResult -> client op60, "dbmmmm" with native-fixed memory blocks.
public sealed class QuestResultRequest : IPacketDeserialize<QuestResultRequest>
{
    public int Pid;
    public byte EquipmentRecordCount;
    public List<EquipmentDurability> Equipment = [];
    public List<WeaponUse> WeaponUses = [];
    public byte[] WeaponUseData = [];
    public byte[] Summary = [];
    public byte[] Detail = [];

    public readonly record struct EquipmentDurability(byte Inventory, byte Cell, byte Durability);
    public readonly record struct WeaponUse(byte Category, ushort Kills);

    public static bool TryDeserialize(ref PacketReader reader, [NotNullWhen(true)] out QuestResultRequest? packet)
    {
        packet = new QuestResultRequest();
        if (!reader.TryRead(out packet.Pid) || !reader.TryRead(out packet.EquipmentRecordCount)
            || packet.EquipmentRecordCount != 9
            || !reader.TryReadExact(27, out var equipment)
            || !reader.TryReadExact(12, out var weaponUse)
            || !reader.TryReadExact(48, out var summary)
            || !reader.TryReadExact(104, out var detail)
            || reader.RemainingLength != 0)
            return false;

        for (int i = 0; i < packet.EquipmentRecordCount; i++)
        {
            int offset = i * 3;
            packet.Equipment.Add(new EquipmentDurability(
                equipment[offset], equipment[offset + 1], equipment[offset + 2]));
        }
        // Native SendQuestResult fills four packed records. Category is derived from the
        // equipped weapon ID and the kill counter is an unaligned little-endian word.
        for (int i = 0; i < 4; i++)
        {
            int offset = i * 3;
            packet.WeaponUses.Add(new WeaponUse(weaponUse[offset],
                BinaryPrimitives.ReadUInt16LittleEndian(weaponUse.Slice(offset + 1, 2))));
        }
        packet.WeaponUseData = weaponUse.ToArray();
        packet.Summary = summary.ToArray();
        packet.Detail = detail.ToArray();
        return true;
    }
}
