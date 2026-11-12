using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

public sealed partial class PlayerStore
{
    // New-character CR. Retail kept it server-side, so it is an operator setting
    // (HUXLEY_STARTING_MONEY, default 10,000).
    public const long MaxMoney = int.MaxValue;

    public static long StartingMoney =>
        long.TryParse(Environment.GetEnvironmentVariable("HUXLEY_STARTING_MONEY"), out long money) && money >= 0
            ? money : 10_000;

    // Character progression; names follow _Player_/_CharInfo_/_PointInfo_ (nLevel, byRank,
    // nBP, nLP, nQT, nMoney, nSkillSlot...). Saved JSON keeps the older property names.
    public sealed class PlayerProgress
    {
        public int Level { get; set; } = 1;

        public byte Rank { get; set; }

        public byte InvenExpandStep { get; set; } = 1;

        public byte BankExpandStep { get; set; } = 1;

        [JsonPropertyName("Xp")] public long XP { get; set; }

        [JsonPropertyName("Bp")] public long BP { get; set; }

        [JsonPropertyName("Lp")] public long LP { get; set; }

        [JsonPropertyName("LevelLpGranted")] public int LevelLPGranted { get; set; }

        public long Money { get; set; } = StartingMoney;

        [JsonPropertyName("Qt")] public long QT { get; set; }

        public int RespawnCount { get; set; }

        // Total time online on this character, in seconds, counted by the server while it is connected.
        public long PlayedSeconds { get; set; }

        // BP from Skirmish matches against bots alone, on SoloBotDay (UTC yyyymmdd).
        public int SoloBotDay { get; set; }

        public long SoloBotBp { get; set; }

        public int AMedalsDonated { get; set; }

        public int BMedalsDonated { get; set; }

        [JsonPropertyName("SkillSlots")] public byte[] SkillSlot { get; set; } = [0, 1, 2, 3, 4];
    }

    // HuxleyMultiPlayerReplicationInfo.ComputePlayerLevelupGoalBP
    public static int ComputePlayerLevelupGoalBP(int currentLevel)
    {
        currentLevel = Math.Clamp(currentLevel, 1, 29);
        float next = currentLevel + 1;
        return (int)(next * next * next * 14f * LevelupBpSubParam[currentLevel - 1]);
    }

    public static int LevelForBP(long bp)
    {
        int level = 1;
        while (level < 30 && ComputePlayerLevelupGoalBP(level) < bp) level++;
        return level;
    }

    // HuxleyQuestGame.ProcessPlayerKilled: a death penalty cannot drop BP below the current
    // level's first value (previous threshold + 1).
    public bool CanApplyMissionBp(int pid, int delta)
    {
        if (pid <= 0) return false;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            int level = LevelForBP(progress.BP);
            long floor = level <= 1 ? 0 : (long)ComputePlayerLevelupGoalBP(level - 1) + 1;
            return (decimal)progress.BP + delta >= floor;
        }
    }

    public PlayerProgress GetProgress(int pid)
    {
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            int expectedLevel = LevelForBP(progress.BP);
            bool changed = progress.Level != expectedLevel;
            progress.Level = expectedLevel;
            changed |= EnsureLevelLp(progress);
            changed |= EnsureSkillSlots(progress);
            changed |= EnsureExpansionSteps(pid, progress);
            if (changed) SaveProgress();
            return progress;
        }
    }

    // Expansion steps can never be below what the stored cells already occupy.
    private bool EnsureExpansionSteps(int pid, PlayerProgress progress)
    {
        byte inventoryStep = (byte)Math.Clamp((int)progress.InvenExpandStep, 1, 2);
        byte bankStep = (byte)Math.Clamp((int)progress.BankExpandStep, 1, 4);
        if (ServerPolicy.MaxExpansionByDefault) { inventoryStep = 2; bankStep = 4; }
        if (_inventory.TryGetValue(pid, out var items))
        {
            if (items.Any(i => i.InventoryIndex == InventoryType.Main && i.InventoryCell >= 48)) inventoryStep = 2;
            int maxBankCell = items.Where(i => i.InventoryIndex == InventoryType.Bank)
                .Select(i => (int)i.InventoryCell).DefaultIfEmpty(-1).Max();
            if (maxBankCell >= 0)
                bankStep = (byte)Math.Max(bankStep, Math.Clamp((maxBankCell / 10) + 1, 1, 4));
        }
        bool changed = progress.InvenExpandStep != inventoryStep || progress.BankExpandStep != bankStep;
        progress.InvenExpandStep = inventoryStep;
        progress.BankExpandStep = bankStep;
        return changed;
    }

    public bool SetRank(int pid, byte rank)
    {
        if (rank > 51) return false;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            if (progress.Rank == rank) return true;
            progress.Rank = rank;
            SaveProgress();
            return true;
        }
    }

    public int MainCapacity(int pid) => GetProgress(pid).InvenExpandStep >= 2 ? 78 : 48;

    public int BankCapacity(int pid) => Math.Clamp(GetProgress(pid).BankExpandStep, (byte)1, (byte)4) * 10;

    private static bool EnsureSkillSlots(PlayerProgress progress)
    {
        if (progress.SkillSlot is { Length: 5 }
            && progress.SkillSlot.OrderBy(x => x).SequenceEqual(new byte[] { 0, 1, 2, 3, 4 }))
            return false;
        progress.SkillSlot = [0, 1, 2, 3, 4];
        return true;
    }

    // One LP per level, plus four on entering B/A/S class at levels 10/20/30.
    private static bool EnsureLevelLp(PlayerProgress progress)
    {
        int entitlement = Math.Max(0, progress.Level - 1);
        foreach (int boundary in new[] { 10, 20, 30 }) if (progress.Level >= boundary) entitlement += 4;
        if (progress.LevelLPGranted >= entitlement) return false;
        progress.LP += entitlement - progress.LevelLPGranted;
        progress.LevelLPGranted = entitlement;
        return true;
    }

    public PlayerProgress AddReward(int pid, long xp, long bp, long money, long lp = 0, long qt = 0)
        => AddReward(pid, xp, bp, money, lp, qt, out _);

    public PlayerProgress AddReward(int pid, long xp, long bp, long money, long lp, long qt,
        out int previousLevel)
    {
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            previousLevel = LevelForBP(progress.BP);
            progress.XP += xp;
            progress.BP += bp;
            progress.Money += money > 0 ? MoneyRoom(progress, money) : money;
            progress.LP += lp;
            progress.QT += qt;
            progress.Level = LevelForBP(progress.BP);
            EnsureLevelLp(progress);
            SaveProgress();
            return progress;
        }
    }

    // How much of `wanted` BP a solo-vs-bots match may still pay today under `dailyLimit`, booked as paid.
    public int TakeSoloBotAllowance(int pid, int wanted, long dailyLimit, int day)
    {
        if (wanted <= 0) return wanted;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            if (progress.SoloBotDay != day)
            {
                progress.SoloBotDay = day;
                progress.SoloBotBp = 0;
            }
            int granted = (int)Math.Max(0, Math.Min(wanted, dailyLimit - progress.SoloBotBp));
            progress.SoloBotBp += granted;
            SaveProgress();
            return granted;
        }
    }

    public void AddPlayedSeconds(IEnumerable<int> pids, int seconds)
    {
        lock (_lock)
        {
            bool any = false;
            foreach (int pid in pids)
            {
                if (pid <= 0) continue;
                ProgressFor(pid).PlayedSeconds += seconds;
                any = true;
            }
            if (any) SaveProgress();
        }
    }

    public bool TrySpendMoney(int pid, int amount)
    {
        if (amount < 0) return false;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            if (progress.Money < amount) return false;
            progress.Money -= amount;
            SaveProgress();
            return true;
        }
    }

    // Credits up to the MaxMoney cap and returns the amount actually credited; callers that must
    // not lose the excess check CanReceiveMoney first and refuse with RESULTTYPE_ITEM_MAXMONEY.
    public long AddMoney(int pid, int amount)
    {
        if (amount <= 0) return 0;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            long credited = MoneyRoom(progress, amount);
            progress.Money += credited;
            SaveProgress();
            return credited;
        }
    }

    // Credits the whole amount or nothing: the cap check and the credit share one lock, so a
    // concurrent credit to the same PID cannot slip between them and truncate this one.
    public bool TryAddMoney(int pid, int amount)
    {
        if (amount <= 0) return amount == 0;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            if (MoneyRoom(progress, amount) != amount) return false;
            progress.Money += amount;
            SaveProgress();
            return true;
        }
    }

    public bool CanReceiveMoney(int pid, long amount)
    {
        if (amount <= 0) return true;
        lock (_lock) return MoneyRoom(ProgressFor(pid), amount) == amount;
    }

    // nMoney is an int on the client: no balance may pass int.MaxValue.
    private static long MoneyRoom(PlayerProgress progress, long amount) =>
        Math.Clamp(MaxMoney - progress.Money, 0, amount);

    public void AddPersonalRespawns(int pid, int count)
    {
        if (count <= 0) return;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            progress.RespawnCount = checked(progress.RespawnCount + count);
            SaveProgress();
        }
    }

    public bool TryUsePersonalRespawn(int pid)
    {
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            if (progress.RespawnCount <= 0) return false;
            progress.RespawnCount--;
            SaveProgress();
            return true;
        }
    }

    // Decided on medal donation (op186): the highest rank whose cumulative Battle Medal and
    // Medal of Valor totals are met, at max level. Each new named rank (EVENT_GROUP_UP) grants
    // LPUP_PER_RANKUP = 1 LP.
    public bool TryPromoteRank(int pid, out byte oldRank, out byte newRank, out int lpGained)
    {
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            oldRank = newRank = progress.Rank;
            lpGained = 0;
            if (LevelForBP(progress.BP) < RankTable.RequiredLevel) return false;
            byte earned = _rankTable.RankFor(progress.AMedalsDonated, progress.BMedalsDonated);
            if (earned <= progress.Rank) return false;
            lpGained = RankTable.GroupStartsCrossed(progress.Rank, earned);
            progress.Rank = earned;
            progress.LP += lpGained;
            newRank = earned;
            SaveProgress();
            return true;
        }
    }

    public bool TrySpendLicensePoints(int pid, int amount)
    {
        if (amount < 0) return false;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            if (progress.LP < amount) return false;
            progress.LP -= amount;
            SaveProgress();
            return true;
        }
    }

    public long AddLicensePoints(int pid, int amount)
    {
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            progress.LP += Math.Max(0, amount);
            SaveProgress();
            return progress.LP;
        }
    }

    public bool SwapSkillSlots(int pid, byte slot1, byte slot2)
    {
        if (slot1 >= 5 || slot2 >= 5) return false;
        lock (_lock)
        {
            var progress = ProgressFor(pid);
            EnsureSkillSlots(progress);
            (progress.SkillSlot[slot1], progress.SkillSlot[slot2]) =
                (progress.SkillSlot[slot2], progress.SkillSlot[slot1]);
            // Native OnUpdateSkillSlot swaps the two INVEN_SKILL entries along with nSkillSlot, so
            // each skill item stays in the button cell mapped to its armor part.
            if (_inventory.TryGetValue(pid, out var list))
            {
                var first = ItemAt(list, InventoryType.Skill, slot1);
                var second = ItemAt(list, InventoryType.Skill, slot2);
                if (first != null) first.InventoryCell = slot2;
                if (second != null) second.InventoryCell = slot1;
                SaveInventory();
            }
            SaveProgress();
            return true;
        }
    }

    public byte[] GetSkillSlots(int pid) => GetProgress(pid).SkillSlot.ToArray();

    // Saves from before SwapSkillSlots moved INVEN_SKILL left socketed skills in their pre-swap
    // cells. Each equipped armor's skill is put back in the cell whose nSkillSlot is its part,
    // trading places with whatever sits there; orphans no equipped armor references stay where
    // they are and EquipSkill may displace them. Runs at load and is a no-op once aligned.
    private void NormalizeSkillCells()
    {
        int realigned = 0;
        bool progressChanged = false;
        foreach (var (pid, list) in _inventory)
        {
            byte[] slots = [0, 1, 2, 3, 4];
            if (_progress.TryGetValue(pid, out var progress))
            {
                progressChanged |= EnsureSkillSlots(progress);
                slots = progress.SkillSlot;
            }
            bool moved = false;
            foreach (var armor in list.Where(i => i.InventoryIndex == InventoryType.Armor && i.InventoryCell < 5)
                         .OrderBy(i => i.InventoryCell).ToList())
            {
                if (armor.Skill == 0 && armor.Skill1 == 0) continue;
                byte target = (byte)Array.IndexOf(slots, armor.InventoryCell);
                bool Socketed(GenericItem? skill) =>
                    skill != null && skill.ItemId != 0 && (skill.ItemId == armor.Skill || skill.ItemId == armor.Skill1);
                var occupant = ItemAt(list, InventoryType.Skill, target);
                if (Socketed(occupant)) continue;
                // Starter armor was saved with its skill on the armor only; the client takes a skill
                // off through its SKILL item, so give it one.
                if (occupant == null && armor.Skill != 0
                    && !list.Any(i => i.InventoryIndex == InventoryType.Skill && i.ItemId == armor.Skill))
                {
                    list.Add(new GenericItem
                    {
                        InventoryIndex = InventoryType.Skill, InventoryCell = target, ItemId = armor.Skill, Count = 1
                    });
                    moved = true;
                    continue;
                }
                // Never take a skill out of a cell that is already aligned with its own armor.
                var misplaced = list.FirstOrDefault(i => i.InventoryIndex == InventoryType.Skill
                    && i.InventoryCell < 5 && Socketed(i) && !IsAlignedSkillCell(list, slots, i));
                if (misplaced == null) continue;
                if (occupant != null) occupant.InventoryCell = misplaced.InventoryCell;
                misplaced.InventoryCell = target;
                moved = true;
            }
            if (!moved) continue;
            realigned++;
            _logger.LogInformation("[Store] realigned PID {0}'s socketed skills with its skill-slot order.", pid);
        }
        if (realigned != 0) SaveInventory();
        if (progressChanged) SaveProgress();
    }

    // Callers must hold _lock (or be loading).
    private static bool IsAlignedSkillCell(List<GenericItem> list, byte[] slots, GenericItem skill)
    {
        var armor = ItemAt(list, InventoryType.Armor, slots[skill.InventoryCell]);
        return armor != null && (armor.Skill == skill.ItemId || armor.Skill1 == skill.ItemId);
    }
}
