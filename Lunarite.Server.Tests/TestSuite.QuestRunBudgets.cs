using System.Buffers.Binary;

using Lunarite.Handlers;
using Lunarite.Packets.Common;
using Lunarite.Storage;
using Lunarite.World;

// Second-round bounds on listen-host reports: the settlement BP allowance, the quest's enemy
// level band, a host's hold on a guest's BP and belt, hourly budgets across rooms, and the
// shutdown flush of debounced kill saves.
internal sealed partial class TestSuite
{
    private void QuestSettlementAndCensusLimits()
    {
        string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        var (host, guest, hostPid, guestPid) = OpenCoopRoom(939890011, "RunBud");
        try
        {
            const int aboveTier = 9051910, inTier = 9050112, otherInTier = 9050111;
            int recommendLevel = _questCatalog.Get(846000)!.RecommendLevel;
            _questStore.Reset(hostPid);
            int Kills() => _questStore.GetProgress(hostPid, 846000).KilledMonsters;

            Game.OnHandlePacket(host.Client, EnemyDownWire(56, aboveTier));
            int afterAboveTier = Kills();
            Game.OnHandlePacket(host.Client, EnemyDownWire(56, inTier));
            Game.OnHandlePacket(host.Client, EnemyDownWire(56, otherInTier));
            int afterInTier = Kills();
            var killReplies = DrainCoop(host);
            Check(_questLoot.RollForEnemy(aboveTier, 0) != null && killReplies.Count(packet => packet[0] == 59) == 3
                && _questLoot.EnemyLevel(aboveTier) == 19 && recommendLevel == 1
                && afterAboveTier == 0 && afterInTier == 2,
                "op56 for an enemy above the quest's level band gets an empty op59; in-band enemies count");

            long bpBefore = _playerStore.GetProgress(hostPid).BP;
            Game.OnHandlePacket(host.Client, SettlementWire(846000, 846000, 2_000_000_000, 0));
            Game.OnHandlePacket(host.Client, SettlementWire(846000, 846000, 2_000_000_000, 0));
            _ = DrainCoop(host);
            long allowance = (long)(_questLoot.EnemyXp(otherInTier) + _questLoot.EnemyXp(inTier)) * QuestInstanceManager.KillBpFactor
                + QuestInstanceManager.RunBonusBp(recommendLevel) + _questRewards.Get(hostPid, 846000).BP;
            long credited = _playerStore.GetProgress(hostPid).BP - bpBefore;
            Check(credited == allowance && credited < PlayerStore.ComputePlayerLevelupGoalBP(20),
                $"a two-kill run's settlements credit only kill BP, the quest-level bonus and the quest's BP ({credited})");

            ConfirmCoopRoom(host, guest);
            int guestStart = PlayerStore.ComputePlayerLevelupGoalBP(9) + 3000;
            _playerStore.AddReward(guestPid, 0, guestStart, 0);
            long penaltyCap = QuestInstanceManager.OtherParticipantDeathsPerRun * QuestInstanceManager.DeathPenaltyBp(10);
            Game.OnHandlePacket(host.Client, SettlementWire(guestPid, 846000, -2000, 0));
            Game.OnHandlePacket(host.Client, SettlementWire(guestPid, 846000, -2000, 0));
            _ = DrainCoop(host);
            _ = DrainCoop(guest);
            long guestLost = guestStart - _playerStore.GetProgress(guestPid).BP;
            Check(penaltyCap > 0 && penaltyCap < 2000 && guestLost == penaltyCap,
                $"a host's op54 takes at most ten deaths' penalty from a guest per run ({guestLost} of {penaltyCap})");

            var guestInventory = _playerStore.GetInventory(guestPid, () => []);
            var belt = new PlayerStore.GenericItem { InventoryIndex = InventoryType.Belt, InventoryCell = 0, ItemId = 604221, Count = 2 };
            guestInventory.Add(belt);
            var reduce = new byte[11];
            reduce[0] = 116;
            BinaryPrimitives.WriteInt32LittleEndian(reduce.AsSpan(1), guestPid);
            reduce[5] = InventoryType.Belt;
            BinaryPrimitives.WriteInt32LittleEndian(reduce.AsSpan(7), 1);
            Game.OnHandlePacket(host.Client, reduce);
            var restocked = _playerStore.GetInventory(guestPid, () => [])
                .Single(item => item.InventoryIndex == InventoryType.Belt && item.InventoryCell == 0);
            restocked.Count = 6;
            for (int i = 0; i < 6; i++) Game.OnHandlePacket(host.Client, reduce);
            _ = DrainCoop(guest);
            int beltLeft = _playerStore.GetInventory(guestPid, () => [])
                .Where(item => item.InventoryIndex == InventoryType.Belt && item.InventoryCell == 0).Sum(item => item.Count);
            Check(beltLeft == 5, $"a host's op116 takes no more of a guest's belt than the guest brought into the run ({beltLeft} left)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            _questInstanceManager.ForgetGuestArrivals(hostPid);
            CloseCoopRoom(host, guest, hostPid, guestPid);
            _questStore.Reset(hostPid);
        }
    }

    private void QuestStoredRunTotalClamped()
    {
        string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        var (host, guest, hostPid, guestPid) = OpenCoopRoom(939890013, "RunTot");
        try
        {
            _questStore.Reset(hostPid);
            _questStore.Acquire(hostPid, 846000);
            var rally = SettlementWire(846000, 846000, 2_000_000_000, 2_000_000_000);
            rally[9] = 1; rally[10] = 2; rally[11] = 1;
            Game.OnHandlePacket(host.Client, rally);
            _ = DrainCoop(host);
            var stored = _questStore.GetProgress(hostPid, 846000);
            Check(stored.BP == QuestInstanceManager.RunBonusBp(_questCatalog.Get(846000)!.RecommendLevel)
                    + _questRewards.Get(hostPid, 846000).BP
                && stored.QT == QuestInstanceManager.RunQtCeiling,
                $"an acquired quest's op54 running total is stored only up to the run allowance ({stored.BP} BP, {stored.QT} QT)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            _questInstanceManager.ForgetGuestArrivals(hostPid);
            CloseCoopRoom(host, guest, hostPid, guestPid);
            _questStore.Reset(hostPid);
        }
    }

    // New rooms (instance tokens) reset the run limits but not a character's hourly budgets.
    private void QuestHourlyBudgetsSurviveRooms()
    {
        const int pid = 939890015, otherPid = 939890017, firstRoom = 939891000;
        long now = DateTime.UtcNow.Ticks;
        long hourLater = now + TimeSpan.TicksPerHour;

        int kills = 0;
        for (int room = 0; room < 60; room++)
            for (int i = 0; i < QuestInstanceManager.EnemyDownBurst; i++)
                if (_questInstanceManager.TryAcceptEnemyDown(firstRoom + room, pid, 10, now)) kills++;
        Check(kills == QuestInstanceManager.EnemyDownsPerHour
            && _questInstanceManager.TryAcceptEnemyDown(firstRoom + 60, otherPid, 10, now)
            && _questInstanceManager.TryAcceptEnemyDown(firstRoom + 61, pid, 10, hourLater),
            $"op56 kills stop at the character's hourly budget across new rooms, then refill ({kills})");

        int drops = 0;
        for (int room = 100; room < 103; room++)
            for (int i = 0; i < QuestInstanceManager.ScriptedDropsPerRun; i++)
                if (_questInstanceManager.TryAcceptScriptedDrop(firstRoom + room, pid, now)) drops++;
        Check(drops == QuestInstanceManager.ScriptedDropsPerHour,
            $"op57 scripted drops stop at the character's hourly budget across new rooms ({drops})");

        long bp = 0, qt = 0;
        for (int room = 200; room < 210; room++)
        {
            var (grantedBp, grantedQt) = _questInstanceManager.ClampRunReward(firstRoom + room, pid,
                2_000_000_000, 2_000_000_000, nowTicks: now);
            bp += grantedBp;
            qt += grantedQt;
        }
        Check(bp == (long)(QuestInstanceManager.BonusRunsPerHour * QuestInstanceManager.RunBonusBp(0))
            && qt == QuestInstanceManager.QtPerHour,
            $"kill-less op54 settlements stop at the character's hourly BP and QT budgets across new rooms ({bp}, {qt})");
    }

    private void QuestStoreFlushesQueuedKills()
    {
        const int pid = 939890019;
        int fieldQuest = _questCatalog.All.First(x => x.QuestType == "QuestType_Free").EntryIndex;
        _questStore.Reset(pid);
        Thread.Sleep(QuestStore.KillSaveDelay + TimeSpan.FromMilliseconds(300)); // earlier tests' queued saves
        int savesBefore = _questStore.SaveCount;
        _questStore.RecordEnemyDown(pid, fieldQuest, authenticatedDirectFieldRun: true);
        _questStore.Flush();
        int flushed = _questStore.SaveCount - savesBefore;
        Thread.Sleep(QuestStore.KillSaveDelay + TimeSpan.FromMilliseconds(300));
        Check(flushed == 1 && _questStore.SaveCount - savesBefore == 1,
            "shutdown flushes a queued op56 kill save at once");
        _questStore.Reset(pid);
    }
}
