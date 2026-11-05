using System;
using System.Collections.Generic;
using System.Linq;

using Lunarite.Network;
using Lunarite.Packets.Common;

namespace Lunarite.Packets.Outgoing;

// Server op42 OnQuestCreate: makes the client host the quest map as a P2P listen server via
// START <HostMap>?RemoteMasterServer?seamless?MinPlayers=<n> (sub_1119CFD0). Solo-host variant;
// layout and decoded offsets are in docs/research/QuestCreate-op42.md.
public class QuestCreate : IPacketSerialize
{
    public byte Answer;              // 0 = OK (host)
    public byte Type;                // 1 reads member block, 4 sets up the P2P socket
    public string HostMap = "";      // quest 'Level' map package name
    public byte[] ConnData = new byte[16];
    public byte MinPlayers = 1;
    public byte Difficulty;

    public int QuestIndex;           // fixed-block field #3 -> the room player's PID
    public byte Character;           // fixed-block field #5 -> species TypeIndex, resolves
                                     // the pawn class (6 = SYN Female)

    // The instance builds its local player from the fixed room-info block (HuxleyQuestGame::PostData).
    // Rank is display only; it does not gate socket skills.
    public string PlayerName = "";   // -> block string fields (#4 URL/Room name + Owner name)
    public int PlayerLevel;          // -> blob B m[20] offset 0
    public byte PlayerStyle;         // -> compact wire CharInfo offset 2
    public byte PlayerRank;          // -> blob B m[20] offset 3
    public int PlayerBP;             // -> blob C m[8]  offset 0
    public int PlayerLP;             // -> native PointInfo offset 4
    public int PlayerMoney;          // -> fixed d field immediately before appearance
    public int PersonalRespawn;      // -> blob E m[8]  offset 0
    public byte InvenExpandStep = 1; // -> native PlayerProperty offset 4
    public byte BankExpandStep = 1;  // -> native PlayerProperty offset 5
    // HuxleyBattleGame.PostData reads the first 25 arData bytes into m_stCustom.
    public Custom Appearance = new();
    // OnQuestCreate copies these arrays into the player wrapper before travel; CMMOVerify
    // serializes them for HuxleyBattleGame.PostData.
    public List<DurativeItem> DurativeItems = [];
    public List<PersonalBuff> PersonalBuffs = [];
    public List<int> Licenses = [];
    // HuxleyBattleGame.PostData reads byPCRoomValid after the licenses; ComputeBonusPoint adds
    // PCROOM_BONUS to mission BP/QT only for a PC-room PRI.
    public bool PcRoomValid;

    // m[5] -> nSkillSlot[i], the armor part supplying socket-skill slot i (OnQuestCreate
    // @0x111305d0). All zeros points every slot at the helmet; identity lets the boots' Sprint work.
    public byte[] SkillSlots = [0, 1, 2, 3, 4];

    // Travel inventory, 32-byte GenericItems; the only gear source in a Battle quest.
    public List<Item> Items = [];
    // Set for clients whose Type-4 branch runs the native backup parser; others get the
    // displaced stream.
    public bool UseNativeP2PBackup;

    public struct Item
    {
        public int ItemID;
        public int Count;
        public byte InvenIndex;   // EINVENTORY (WEAPON=3, ARMOR=4, ...)
        public byte Cell;
        public byte State;
        public int LimitTime;
        public int TuneUpOptions;
        public int Skill0;        // nGeneral1 (armor socket skill)
        public int Skill1;        // nGeneral2
        public int Durability;    // nGeneral3
    }

    // OP42_PROBE=1 fills each blob with a distinct byte range so the client's PostData log shows
    // which byte feeds each field.
    private static bool Probing =>
        System.Environment.GetEnvironmentVariable("OP42_PROBE") == "1";

    // base, base+1, ... when probing, otherwise zeros.
    private static byte[] Probe(int size, int @base)
    {
        var b = new byte[size];
        if (Probing)
            for (int i = 0; i < size; i++)
                b[i] = (byte)(@base + i);
        return b;
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)42);

        writer.Write(Answer);

        if (Answer != 0)
            return writer.Buffer; // client shows an error and stops parsing

        WriteTravelHeader(writer);
        WriteRoomInfo(writer);
        WriteNativeArrays(writer);
        WriteItemStream(writer);

        return writer.Buffer;
    }

    private void WriteTravelHeader(PacketWriter writer)
    {
        writer.Write(Type);
        writer.Write(HostMap);
        writer.Write(ConnData);
        writer.Write(MinPlayers);
        writer.Write(Difficulty);

        if (Type == 1)
        {
            writer.Write((byte)0);       // member/session flag
            writer.Write((byte)0);       // member count (solo)
        }
    }

    // Fixed room-info block: d b d s b b d m[25] m[20] m[8] m[5] s b m[8]
    private void WriteRoomInfo(PacketWriter writer)
    {
        writer.Write(0);                 // d: room/quest metadata
        writer.Write((byte)0);           // b
        writer.Write(QuestIndex);        // d  (field #3 -> URL PID)
        writer.Write(PlayerName);        // s  (field #4 -> URL Name)
        writer.Write(Character);         // b  (field #5 -> URL Character / species TypeIndex)
        writer.Write((byte)0);           // b  (field #6)
        writer.Write(PlayerMoney);       // d: m_stPlayer.nMoney (v102)
        BuildPlayerBlobs(out var blobA, out var blobB, out var blobC, out var blobE);
        writer.Write(blobA);             // m[25] blob A
        writer.Write(blobB);             // m[20] blob B
        writer.Write(blobC);             // m[8]  blob C
        writer.Write(SkillSlots.Length == 5 ? SkillSlots : [0, 1, 2, 3, 4]);  // m[5] = nSkillSlot[] (socket-skill armor-part map)
        writer.Write(PlayerName);        // s (owner name)
        writer.Write((byte)0);           // b
        writer.Write(blobE);             // m[8]  blob E
    }

    private void BuildPlayerBlobs(out byte[] blobA, out byte[] blobB, out byte[] blobC, out byte[] blobE)
    {
        blobA = Probe(25, 1);            // m[25] blob A (v103) - m_stCustom appearance
        blobB = Probe(20, 60);           // m[20] blob B (v94)  - Level@0 (16-bit LE), Rank@3 (byte)
        blobC = Probe(8, 90);            // m[8]  blob C (v101) - BP@0 (int32 LE)
        blobE = Probe(8, 110);           // m[8]  blob E (v100) - PersonalRespawn@0 (int32 LE)
        if (Probing) return;
        using (var appearanceWriter = new PacketWriter())
        {
            Appearance.Serialize(appearanceWriter);
            blobA = appearanceWriter.Buffer;
        }
        blobB[0] = (byte)(PlayerLevel & 0xFF);
        blobB[1] = (byte)((PlayerLevel >> 8) & 0xFF);
        blobB[2] = PlayerStyle;
        blobB[3] = PlayerRank;
        blobC[0] = (byte)(PlayerBP & 0xFF);
        blobC[1] = (byte)((PlayerBP >> 8) & 0xFF);
        blobC[2] = (byte)((PlayerBP >> 16) & 0xFF);
        blobC[3] = (byte)((PlayerBP >> 24) & 0xFF);
        blobC[4] = (byte)(PlayerLP & 0xFF);
        blobC[5] = (byte)((PlayerLP >> 8) & 0xFF);
        blobC[6] = (byte)((PlayerLP >> 16) & 0xFF);
        blobC[7] = (byte)((PlayerLP >> 24) & 0xFF);
        blobE[0] = (byte)(PersonalRespawn & 0xFF);
        blobE[1] = (byte)((PersonalRespawn >> 8) & 0xFF);
        blobE[2] = (byte)((PersonalRespawn >> 16) & 0xFF);
        blobE[3] = (byte)((PersonalRespawn >> 24) & 0xFF);
        blobE[4] = InvenExpandStep;
        blobE[5] = BankExpandStep;
    }

    // Native OnQuestCreate arrays: 20-byte DurativeItem, 12-byte PersonalBuff, int license.
    private void WriteNativeArrays(PacketWriter writer)
    {
        writer.Write(DurativeItems.Count); // d countR
        foreach (var item in DurativeItems) item.Serialize(writer);
        int buffCount = Math.Min(byte.MaxValue, PersonalBuffs.Count);
        writer.Write((byte)buffCount);    // b countS
        foreach (var buff in PersonalBuffs.Take(buffCount)) buff.Serialize(writer);
        int licenseCount = Math.Min(byte.MaxValue, Licenses.Count);
        writer.Write((byte)licenseCount); // b countT
        foreach (int license in Licenses.Take(licenseCount)) writer.Write(license);
        writer.Write((byte)(PcRoomValid ? 1 : 0)); // b byPCRoomValid (v83[0])
    }

    // Item stream: 7 per-inventory counts (EINVENTORY 0..6), then sum(counts) 32-byte
    // GenericItems, each placed by its own InvenIndex/Cell.
    private void WriteItemStream(PacketWriter writer)
    {
        if (Type != 4 || UseNativeP2PBackup)
        {
            // CQuestGamePlayerBackupInfo: type, BackupLevelBP, quest-item count. Its URL
            // serializer emits an int32 item count before the inventory records. BackupLevelBP
            // seeds the run's LevelBP (signed 16-bit), so a fresh room starts it at 0.
            writer.Write((byte)1);
            writer.Write((ushort)0);
            writer.Write((byte)0);
        }
        QuestTravelItemStream.Write(writer, Items, displacedP2P: Type == 4 && !UseNativeP2PBackup);
    }
}
