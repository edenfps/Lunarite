using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// dsbbddmmmmm
// dd x2
// sdb
// d
// m
// b
// m
// b
// m
public class PlayerInfo : IPacketSerialize
{
    // Exported F_Player_ defaults USERSTATUS_NORMAL to numeric 1.
    public const byte NormalUserStatus = 1;
    // UHuxleySubsystem::IsAdmin (sub_111C1720) treats nUserStatus >= 100 as GM.
    public const byte GmUserStatus = 100;

    public int Pid;

    public string? Name;

    public byte Index;
    public byte UserStatus = NormalUserStatus;

    public int Money;
    public int QT;

    public Custom Custom = new();
    public CharInfo CharInfo = new();
    public PointInfo PointInfo = new();
    public PlayerProperty PlayerProperty = new();

    public byte[] SkillSlots = new byte[5];

    public MedalInfo[] Medals = new MedalInfo[2];

    public string? ClanName;
    public int ClanMark;
    public byte ClanRank;

    public List<DurativeItem> DurativeItems = [];
    public List<PersonalBuff> PersonalBuffs = [];
    public List<int> LicenseList = [];
    public List<Item> Items = [];

    public sealed class Item
    {
        public int ItemID;
        public int Count = 1;
        public byte InventoryIndex;
        public byte Cell;
        public byte State;
        public int LimitTime;
        public int TuneUpOptions;
        public int Skill0;
        public int Skill1;
        public int Durability;
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)11);

        writer.Write(Pid);
        writer.Write(Name);
        writer.Write(Index);
        writer.Write(UserStatus);
        writer.Write(Money);
        writer.Write(QT);

        Custom.Serialize(writer);
        CharInfo.Serialize(writer);
        PointInfo.Serialize(writer);
        PlayerProperty.Serialize(writer);

        for (var i = 0; i < SkillSlots.Length; i++)
            writer.Write(SkillSlots[i]);

        for (var i = 0; i < Medals.Length; i++)
        {
            var medalInfo = Medals[i];

            writer.Write(medalInfo.MedalID);
            writer.Write(medalInfo.MedalCount);
        }

        writer.Write(ClanName);
        writer.Write(ClanMark);
        writer.Write(ClanRank);

        writer.Write(DurativeItems.Count);

        foreach (var durativeItem in DurativeItems)
            durativeItem.Serialize(writer);

        int personalBuffCount = Math.Min(byte.MaxValue, PersonalBuffs.Count);
        writer.Write((byte)personalBuffCount);

        foreach (var personalBuff in PersonalBuffs.Take(personalBuffCount))
            personalBuff.Serialize(writer);

        int licenseCount = Math.Min(byte.MaxValue, LicenseList.Count);
        writer.Write((byte)licenseCount);

        foreach (var licenseItem in LicenseList.Take(licenseCount))
            writer.Write(licenseItem);

        // Native op11 ends with seven per-inventory counts followed by ordinary
        // 32-byte GenericItem records (the op82 body without its opcode).
        var counts = new byte[7];
        foreach (var item in Items)
            if (item.InventoryIndex < counts.Length && counts[item.InventoryIndex] < byte.MaxValue)
                counts[item.InventoryIndex]++;
        writer.Write(counts);
        foreach (var item in Items)
        {
            if (item.InventoryIndex >= counts.Length) continue;
            writer.Write(item.ItemID);
            writer.Write(item.Count);
            writer.Write(item.InventoryIndex);
            writer.Write(item.Cell);
            writer.Write(item.State);
            writer.Write((byte)0);
            writer.Write(item.LimitTime);
            writer.Write(item.TuneUpOptions);
            writer.Write(item.Skill0);
            writer.Write(item.Skill1);
            writer.Write(item.Durability);
        }

        return writer.Buffer;
    }
}
