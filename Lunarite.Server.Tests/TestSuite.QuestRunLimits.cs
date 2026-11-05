using System.Buffers.Binary;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Storage;
using Lunarite.World;

// Per-run bounds on a listen host's loot, settlement and gear reports, and entry-item refunds when
// a room is left before op199.
internal sealed partial class TestSuite
{
    private const int RunLimitEntryItem = 604222;

    private static byte[] EnemyDownWire(int opcode, int index)
    {
        var wire = new byte[5];
        wire[0] = (byte)opcode;
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(1), index);
        return wire;
    }

    private static byte[] SettlementWire(int wirePid, int questIndex, int bp, int qt)
    {
        var wire = new byte[36];
        wire[0] = 54;
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(1), wirePid);
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(5), questIndex);
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(16), bp);
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(20), qt);
        return wire;
    }

    private static byte[] QuestResultWire(int wirePid, byte bootsDurability)
    {
        var wire = new byte[197];
        wire[0] = 60;
        BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(1), wirePid);
        wire[5] = 9;
        wire[6] = InventoryType.Armor; wire[7] = 4; wire[8] = bootsDurability;
        return wire;
    }

    private int MainCount(int pid, int itemId) => _playerStore.GetInventory(pid, () => [])
        .Where(item => item.InventoryIndex == InventoryType.Main && item.ItemId == itemId).Sum(item => item.Count);

    // op199 from the host: renewed op55 to the host, op128/op125/op52 to the guest.
    private void ConfirmCoopRoom(CoopLink host, CoopLink guest)
    {
        using var ready = new PacketWriter();
        ready.Write((byte)199); ready.Write(846000);
        Game.OnHandlePacket(host.Client, ready.Buffer);
        _ = DrainCoop(host);
        _ = DrainCoop(guest);
    }

    private void QuestRunReportLimits()
    {
        string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        var (host, guest, hostPid, guestPid) = OpenCoopRoom(939880001, "RunCap");
        try
        {
            const int enemy = 9050111;
            _questStore.Reset(hostPid);
            // 40 tokens take 10 s to refill, far longer than the burst takes to send.
            int forged = QuestInstanceManager.EnemyDownBurst + 40;
            for (int i = 0; i < forged; i++) Game.OnHandlePacket(host.Client, EnemyDownWire(56, enemy));
            var killReplies = DrainCoop(host);
            int kills = _questStore.GetProgress(hostPid, 846000).KilledMonsters;
            Check(killReplies.Count(packet => packet[0] == 59) == forged
                && kills >= QuestInstanceManager.EnemyDownBurst && kills < QuestInstanceManager.EnemyDownBurst + 5,
                "an op56 flood beyond the run's kill-report rate gets empty op59 replies and records no kills");

            for (int i = 0; i < QuestInstanceManager.ScriptedDropsPerRun; i++)
                Game.OnHandlePacket(host.Client, EnemyDownWire(57, 82165));
            var dropReplies = DrainCoop(host);
            Game.OnHandlePacket(host.Client, EnemyDownWire(57, 82165));
            var overLimit = ReceiveCoop(host);
            Check(dropReplies.Count == QuestInstanceManager.ScriptedDropsPerRun
                && dropReplies.All(packet => packet[0] == 60 && packet[5] == 1)
                && overLimit[0] == 60 && overLimit[5] == 0,
                "op57 scripted drops stop at the cooked maps' SpawnItem count for one run");

            long bpBefore = _playerStore.GetProgress(hostPid).BP;
            long qtBefore = _playerStore.GetProgress(hostPid).QT;
            Game.OnHandlePacket(host.Client, SettlementWire(846000, 846000, 2_000_000_000, 2_000_000_000));
            Game.OnHandlePacket(host.Client, SettlementWire(846000, 846000, 2_000_000_000, 2_000_000_000));
            _ = DrainCoop(host);
            long bpCeiling = QuestInstanceManager.RunBonusBp(_questCatalog.Get(846000)!.RecommendLevel)
                + _questRewards.Get(hostPid, 846000).BP
                + (long)kills * _questLoot.EnemyXp(enemy) * QuestInstanceManager.KillBpFactor;
            Check(_questLoot.EnemyXp(enemy) > 0
                && _playerStore.GetProgress(hostPid).BP - bpBefore == bpCeiling
                && _playerStore.GetProgress(hostPid).QT - qtBefore == QuestInstanceManager.RunQtCeiling,
                "op54 settlements credit BP and QT only up to the run's table-derived allowance");

            var guestInventory = _playerStore.GetInventory(guestPid, () => []);
            guestInventory.Add(new PlayerStore.GenericItem { InventoryIndex = InventoryType.Armor, InventoryCell = 4, ItemId = 200299, Durability = 100 });
            guestInventory.Add(new PlayerStore.GenericItem { InventoryIndex = InventoryType.Belt, InventoryCell = 0, ItemId = 604221, Count = 3 });
            ConfirmCoopRoom(host, guest);

            var forgedReduce = new byte[11];
            forgedReduce[0] = 116;
            BinaryPrimitives.WriteInt32LittleEndian(forgedReduce.AsSpan(1), guestPid);
            forgedReduce[5] = InventoryType.Armor; forgedReduce[6] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(forgedReduce.AsSpan(7), 1);
            Game.OnHandlePacket(host.Client, forgedReduce);
            var beltReduce = forgedReduce.ToArray();
            beltReduce[5] = InventoryType.Belt;
            Game.OnHandlePacket(host.Client, beltReduce);
            var guestItems = _playerStore.GetInventory(guestPid, () => []);
            Check(guestItems.Any(item => item.InventoryIndex == InventoryType.Armor && item.InventoryCell == 0 && item.ItemId == 200299)
                && guestItems.Single(item => item.InventoryIndex == InventoryType.Belt && item.InventoryCell == 0).Count == 2,
                "op116 only reduces one item of the guest's belt cell 0, as the special skill sends it");

            byte BootsOf(int pid) => (byte)_playerStore.GetInventory(pid, () => [])
                .Single(item => item.InventoryIndex == InventoryType.Armor && item.InventoryCell == 4).Durability;
            Game.OnHandlePacket(host.Client, QuestResultWire(guestPid, 0));
            byte afterOne = BootsOf(guestPid);
            for (int i = 0; i < 3; i++) Game.OnHandlePacket(host.Client, QuestResultWire(guestPid, 0));
            Check(afterOne == 100 - (9 + QuestInstanceManager.OtherParticipantDeathWear)
                && BootsOf(guestPid) == 100 - QuestInstanceManager.OtherParticipantWearPerRun,
                $"a host's op60 wears a guest's gear by at most one result's worth and half the scale per run (actual {afterOne}/{BootsOf(guestPid)})");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            _questInstanceManager.ForgetGuestArrivals(hostPid);
            CloseCoopRoom(host, guest, hostPid, guestPid);
        }
    }

    private void PendingQuestRoomRefunds()
    {
        string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        var (host, guest, hostPid, guestPid) = OpenCoopRoom(939880003, "RunLeave");
        try
        {
            _questInstanceManager.SetEntryReservation(hostPid, new QuestInstanceManager.QuestEntryReservation
            {
                Members = [hostPid, guestPid], Items = [(RunLimitEntryItem, 1)]
            });
            Game.OnHandlePacket(guest.Client, new byte[] { 120 });
            Check(MainCount(guestPid, RunLimitEntryItem) == 1 && MainCount(hostPid, RunLimitEntryItem) == 0
                && _questInstanceManager.HasTransport(hostPid),
                "a guest leaving a pending room gets back only its own entry items and the host's room carries on");
            ConfirmCoopRoom(host, guest);
            Check(!_questInstanceManager.TryTakeQuestEntryReservationForMember(hostPid, out _, out _)
                && MainCount(hostPid, RunLimitEntryItem) == 0,
                "the host's op199 then spends its own entry items");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            _questEntryReservationStore.Remove(hostPid);
            _questInstanceManager.ForgetGuestArrivals(hostPid);
            CloseCoopRoom(host, guest, hostPid, guestPid);
        }

        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        (host, guest, hostPid, guestPid) = OpenCoopRoom(939880005, "RunLoot");
        try
        {
            _questInstanceManager.SetEntryReservation(hostPid, new QuestInstanceManager.QuestEntryReservation
            {
                Members = [hostPid, guestPid], Items = [(RunLimitEntryItem, 1)]
            });
            Game.OnHandlePacket(host.Client, EnemyDownWire(56, 9050111));
            _ = DrainCoop(host);
            DisconnectHandler.HandleDisconnect(host.Client);
            Check(MainCount(hostPid, RunLimitEntryItem) == 0 && MainCount(guestPid, RunLimitEntryItem) == 1,
                "a host that looted before op199 keeps its entry items spent when it abandons the room");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            _questEntryReservationStore.Remove(hostPid);
            CloseCoopRoom(host, guest, hostPid, guestPid);
        }
    }

    private void KillReportsSaveDebounced()
    {
        const int pid = 939880007;
        int fieldQuest = _questCatalog.All.First(x => x.QuestType == "QuestType_Free").EntryIndex;
        _questStore.Reset(pid);
        Thread.Sleep(QuestStore.KillSaveDelay + TimeSpan.FromMilliseconds(300)); // earlier tests' queued saves
        int savesBefore = _questStore.SaveCount;
        for (int i = 0; i < 50; i++) _questStore.RecordEnemyDown(pid, fieldQuest, authenticatedDirectFieldRun: true);
        int burstSaves = _questStore.SaveCount - savesBefore;
        Thread.Sleep(QuestStore.KillSaveDelay + TimeSpan.FromMilliseconds(500));
        Check(burstSaves == 0 && _questStore.SaveCount - savesBefore == 1
            && _questStore.GetProgress(pid, fieldQuest).KilledMonsters == 50,
            "a burst of op56 kill reports writes the quest store once, not once per kill");
        _questStore.Reset(pid);
    }
}
