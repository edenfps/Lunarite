using System.Buffers.Binary;

using Lunarite.Handlers;
using Lunarite.Storage;
using Lunarite.World;

// Third-round bounds on listen-host reports: BP allowances scaled to the run's quest level, and
// quest completion (op45) booking a stored running total against the run and hourly budgets.
internal sealed partial class TestSuite
{
    private void QuestRunAllowancesScaleWithQuestLevel()
    {
        string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        var (host, guest, hostPid, guestPid) = OpenCoopRoom(939890031, "Tier");
        try
        {
            // 9103609 is the table's 2,000 XP level-36 enemy; 9021118 a 498 XP level-11 boss,
            // inside a RecommendLevel 1 quest's band.
            const int topEnemy = 9103609, tierBoss = 9021118;
            int recommendLevel = _questCatalog.Get(846000)!.RecommendLevel;
            long questBp = _questRewards.Get(hostPid, 846000).BP;
            _questStore.Reset(hostPid);

            long bpBefore = _playerStore.GetProgress(hostPid).BP;
            Game.OnHandlePacket(host.Client, SettlementWire(846000, 846000, 2_000_000_000, 0));
            _ = DrainCoop(host);
            long zeroKill = _playerStore.GetProgress(hostPid).BP - bpBefore;
            Check(recommendLevel == 1 && zeroKill == QuestInstanceManager.RunBonusBp(recommendLevel) + questBp
                && PlayerStore.LevelForBP(_playerStore.GetProgress(hostPid).BP) <= 2,
                $"a zero-kill settlement of a level 1 mission credits one level-1 span, not a fixed Kismet bonus ({zeroKill})");

            Game.OnHandlePacket(host.Client, EnemyDownWire(56, topEnemy));
            int afterTopEnemy = _questStore.GetProgress(hostPid, 846000).KilledMonsters;
            for (int i = 0; i < QuestInstanceManager.EnemyDownBurst - 1; i++)
                Game.OnHandlePacket(host.Client, EnemyDownWire(56, tierBoss));
            _ = DrainCoop(host);
            bpBefore = _playerStore.GetProgress(hostPid).BP;
            Game.OnHandlePacket(host.Client, SettlementWire(846000, 846000, 2_000_000_000, 0));
            _ = DrainCoop(host);
            long killCredit = _playerStore.GetProgress(hostPid).BP - bpBefore;
            Check(_questLoot.EnemyXp(topEnemy) == 2000 && _questLoot.EnemyLevel(tierBoss) == 11 && afterTopEnemy == 0
                && killCredit == QuestInstanceManager.RunKillBpCeiling(recommendLevel)
                && PlayerStore.LevelForBP(_playerStore.GetProgress(hostPid).BP) <= 12,
                $"forged boss kills in a level 1 mission credit at most the run's kill BP cap ({killCredit})");

            // FQ-846203 (RecommendLevel 35) has no entry condition: a level 1 character opening it
            // gets the allowances of level 12, not of the level 30 tier.
            int ungated = QuestInstanceManager.AllowanceLevel(35, 1);
            Check(ungated == 1 + QuestInstanceManager.QuestEnemyLevelMargin
                && QuestInstanceManager.AllowanceLevel(1, 30) == 1
                && QuestInstanceManager.RunBonusBp(ungated) < QuestInstanceManager.RunBonusBp(35),
                "an ungated high-tier mission opened by a low-level character uses the character's level for BP allowances");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            _questInstanceManager.ForgetGuestArrivals(hostPid);
            CloseCoopRoom(host, guest, hostPid, guestPid);
            _questStore.Reset(hostPid);
        }
    }

    private void QuestCompletionBooksStoredRunTotal()
    {
        string? previousGatewaySetting = Environment.GetEnvironmentVariable("QUEST_GATEWAY_DISABLED");
        Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", "1");
        var (host, guest, hostPid, guestPid) = OpenCoopRoom(939890033, "Book");
        try
        {
            _questStore.Reset(hostPid);
            _questStore.Acquire(hostPid, 846000);
            var rewardRally = SettlementWire(846000, 846000, 2_000_000_000, 2_000_000_000);
            rewardRally[9] = 1; rewardRally[10] = 1; rewardRally[11] = 2;
            Game.OnHandlePacket(host.Client, rewardRally);
            _ = DrainCoop(host);
            var stored = _questStore.GetProgress(hostPid, 846000);

            long bpBefore = _playerStore.GetProgress(hostPid).BP;
            long qtBefore = _playerStore.GetProgress(hostPid).QT;
            var complete = new byte[6];
            complete[0] = 45;
            BinaryPrimitives.WriteInt32LittleEndian(complete.AsSpan(1), 846000);
            Game.OnHandlePacket(host.Client, complete);
            _ = DrainCoop(host);
            long paidBp = _playerStore.GetProgress(hostPid).BP - bpBefore;
            long paidQt = _playerStore.GetProgress(hostPid).QT - qtBefore;
            var left = _questInstanceManager.ClampRunReward(hostPid, hostPid, 2_000_000_000, 2_000_000_000, book: false);
            Check(_questStore.Completed(hostPid).Contains(846000) && stored.BP > 0 && stored.QT > 0
                && stored.RunInstance == hostPid && paidBp == _questRewards.Get(hostPid, 846000).BP + stored.BP
                && paidQt == stored.QT && left == (0, 0),
                $"op45 pays a stored rally total by booking it against the run's allowance ({paidBp} BP, {paidQt} QT, left {left})");
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUEST_GATEWAY_DISABLED", previousGatewaySetting);
            _questInstanceManager.ForgetGuestArrivals(hostPid);
            CloseCoopRoom(host, guest, hostPid, guestPid);
            _questStore.Reset(hostPid);
        }
    }
}
