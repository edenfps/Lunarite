using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// Server op55: adds a PID + verify KEY to the host's MMOPermission table so a party member or
// the ?MasterServer gateway passes the MMO HELLO check.
//
// Handler sub_111C0920 parses "mdbdsbbdmmmmsbm" plus trailing arrays, builds a record via
// sub_11196A50 (key at record[1..4]) and inserts it under the third field via
// sub_11196FD0(PID, record). UWorld::NotifyReceivedText checks HELLO PID+KEY against it
// (sub_11197060). The body is op42's room-info block, arrays and item counts, prefixed by the
// 16-byte KEY and without op42's Answer/Type/HostMap/ConnData/MinPlayers/Difficulty header.
public class QuestPermissionRegister : IPacketSerialize
{
    protected virtual byte Opcode => 55;
    // HuxleyQuestGame.ParsePlayerBackupData reads a type-1 backup (level BP and carried quest
    // items) from the PostData stream; HuxleyBattleGame's only logs SKIP, so a battle record
    // must carry none or every item after it is read out of place.
    protected virtual bool CarriesQuestBackup => true;
    public byte[] Key = new byte[16];  // the 16-byte verify key (== op42.ConnData)
    public int Pid;                    // insert key; must match the gateway HELLO's PID
    public string PlayerName = "";
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
    public bool PcRoomValid;           // PostData byPCRoomValid (PC-room mission bonus)
    public List<QuestCreate.Item> Items = [];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(Opcode);

        writer.Write(Key);               // m[16] verify key (record[1..4])

        // Fixed room-info block, same order as op42: d b d s b b d m25 m20 m8 m5 s b m8
        writer.Write(0);                 // d  field #1
        writer.Write((byte)0);           // b  field #2
        writer.Write(Pid);               // d  field #3 -> table insert PID
        writer.Write(PlayerName);        // s  field #4
        writer.Write(Character);         // b  field #5 (Character)
        writer.Write((byte)0);           // b  field #6
        writer.Write(Money);             // d  field #7
        byte[] appearanceBytes;
        using (var appearance = new PacketWriter())
        {
            Appearance.Serialize(appearance);
            appearanceBytes = appearance.Buffer;
        }
        writer.Write(appearanceBytes);   // m[25]
        var charInfo = new byte[20];
        charInfo[0] = (byte)Level;
        charInfo[1] = (byte)(Level >> 8);
        charInfo[2] = Style;
        charInfo[3] = Rank;
        writer.Write(charInfo);          // m[20]
        var points = new byte[8];
        BitConverter.GetBytes(BP).CopyTo(points, 0);
        BitConverter.GetBytes(LP).CopyTo(points, 4);
        writer.Write(points);            // m[8]
        writer.Write(SkillSlots.Length == 5 ? SkillSlots : [0, 1, 2, 3, 4]);
        writer.Write(PlayerName);        // s  (owner name)
        writer.Write((byte)0);           // b
        var property = new byte[8];
        BitConverter.GetBytes(RespawnCount).CopyTo(property, 0);
        property[4] = InvenExpandStep;
        property[5] = BankExpandStep;
        writer.Write(property);          // m[8]

        // Variable arrays, all empty.
        writer.Write(DurativeItems.Count);
        foreach (var item in DurativeItems) item.Serialize(writer);
        int buffCount = Math.Min(byte.MaxValue, PersonalBuffs.Count);
        writer.Write((byte)buffCount);
        foreach (var buff in PersonalBuffs.Take(buffCount)) buff.Serialize(writer);
        int licenseCount = Math.Min(byte.MaxValue, Licenses.Count);
        writer.Write((byte)licenseCount);
        foreach (int license in Licenses.Take(licenseCount)) writer.Write(license);
        writer.Write((byte)(PcRoomValid ? 1 : 0)); // b byPCRoomValid

        // This parser always calls sub_11196CD0 before the seven inventory counts.
        if (CarriesQuestBackup)
        {
            writer.Write((byte)1);       // CQuestGamePlayerBackupInfo
            writer.Write((ushort)0);     // BackupLevelBP: the run's LevelBP, 0 in a fresh room
            writer.Write((byte)0);       // zero carried quest-item pairs
        }
        else writer.Write((byte)0);      // no backup
        QuestTravelItemStream.Write(writer, Items);

        return writer.Buffer;
    }
}
