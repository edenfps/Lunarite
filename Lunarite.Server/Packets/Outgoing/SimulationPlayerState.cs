using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

public sealed class SimulationPlayerState
{
    public byte[] Key = new byte[16];
    public int RoomId;
    public byte Squad;
    public int Pid;
    public string Name = "";
    public byte Character;
    public int Money;
    public Custom Appearance = new();
    public int Level;
    public byte Style;
    public byte Rank;
    public int BP;
    public int LP;
    public byte[] SkillSlots = [0, 1, 2, 3, 4];
    public int RespawnCount;
    public byte InvenExpandStep = 1;
    public byte BankExpandStep = 1;
    public List<DurativeItem> DurativeItems = [];
    public List<PersonalBuff> PersonalBuffs = [];
    public List<int> Licenses = [];
    public List<QuestCreate.Item> Items = [];
    // Last op152 battle record of this player (31 bytes, PID first), restored by a new host.
    public byte[]? BattleBackup;

    internal void WritePermissionBody(PacketWriter writer, bool includeKey)
    {
        if (includeKey) writer.Write(Key);
        writer.Write(RoomId); writer.Write(Squad); writer.Write(Pid); writer.Write(Name);
        writer.Write(Character); writer.Write((byte)0); writer.Write(Money);
        using (var custom = new PacketWriter()) { Appearance.Serialize(custom); writer.Write(custom.Buffer); }
        byte[] info = new byte[20];
        BitConverter.GetBytes((ushort)Math.Clamp(Level, 0, ushort.MaxValue)).CopyTo(info, 0);
        info[2] = Style; info[3] = Rank; writer.Write(info);
        byte[] points = new byte[8]; BitConverter.GetBytes(BP).CopyTo(points, 0); BitConverter.GetBytes(LP).CopyTo(points, 4);
        writer.Write(points); writer.Write(SkillSlots.Length == 5 ? SkillSlots : [0, 1, 2, 3, 4]);
        writer.Write(Name); writer.Write((byte)0);
        byte[] property = new byte[8]; BitConverter.GetBytes(RespawnCount).CopyTo(property, 0);
        property[4] = InvenExpandStep; property[5] = BankExpandStep; writer.Write(property);
        writer.Write(DurativeItems.Count); foreach (var value in DurativeItems) value.Serialize(writer);
        int buffs = Math.Min(byte.MaxValue, PersonalBuffs.Count); writer.Write((byte)buffs);
        foreach (var value in PersonalBuffs.Take(buffs)) value.Serialize(writer);
        int licenses = Math.Min(byte.MaxValue, Licenses.Count); writer.Write((byte)licenses);
        foreach (int value in Licenses.Take(licenses)) writer.Write(value);
        // Native op155 (sub_11131170) and op157 (sub_111C0920, shared with quest op55) read
        // this trailing byte, a CQuestGamePlayerBackupInfo type byte (sub_11196CD0; 0 = none),
        // seven inventory counts, then raw 32-byte GenericItem records.
        // Type 2 (CBattleGamePlayerBackupInfo, sub_11193A50) is followed by the raw 31-byte record.
        if (BattleBackup is { Length: 31 })
        {
            writer.Write((byte)2);
            writer.Write(BattleBackup);
        }
        else writer.Write((byte)0);
        writer.Write((byte)0);
        QuestTravelItemStream.Write(writer, Items);
    }
}
