using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Storage;

namespace Lunarite.Packets.Outgoing;

// Native OnAddPlayer fixed decoder: `dsbMmmbbddsb`, followed by one byte armor
// count and that many 12-byte GenericItemBase records (cell, item ID, general3).
// sub_11116900 expands each compact record to a GenericItem and forces inventory 4.
public sealed class AddPlayer : IPacketSerialize
{
    public int Pid;
    public string Name = "";
    public byte Index;
    public Custom Custom = new();
    public float X;
    public float Y;
    public float Z;
    public int Pitch;
    public int Yaw;
    public int Roll;
    public byte StartSpot = byte.MaxValue; // native script treats 0xff as INDEX_NONE/-1
    public byte Style;
    public int Level = 1;
    public int VehicleIndex;
    public string ClanName = "";
    public byte UserStatus = PlayerInfo.NormalUserStatus;
    public List<PlayerStore.GenericItem> EquippedItems = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)20);
        writer.Write(Pid); writer.Write(Name); writer.Write(Index);
        Custom.Serialize(writer); // M, 25 bytes
        writer.Write(X); writer.Write(Y); writer.Write(Z); // m, 12 bytes
        writer.Write(Pitch); writer.Write(Yaw); writer.Write(Roll); // m, 12 bytes
        writer.Write(StartSpot); writer.Write(Style); writer.Write(Level);
        writer.Write(VehicleIndex); writer.Write(ClanName); writer.Write(UserStatus);
        int armorCount = Math.Min(byte.MaxValue, EquippedItems.Count);
        writer.Write((byte)armorCount);
        foreach (var item in EquippedItems.Take(armorCount))
        {
            writer.Write(item.InventoryCell);
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write(item.ItemId);
            writer.Write(item.Durability);
        }
        return writer.Buffer;
    }
}
