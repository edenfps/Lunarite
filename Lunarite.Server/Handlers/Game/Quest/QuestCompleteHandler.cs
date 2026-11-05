using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// US-client binary: SendQuestComplete -> opcode 45, format "db" (quest id, reward-item slot).
// Server response opcode 49 parses "bd" and invokes OnQuestComplete(answer, quest id).
[PacketHandler]
public static class QuestCompleteHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static QuestCatalog _questCatalog = null!;
    private static CharacterManager _characterManager = null!;
    private static CityManager _cityManager = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ItemManager _itemManager = null!;
    private static QuestManager _questManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestRewards _questRewards = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestCompleteHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questRewards = serviceProvider.GetRequiredService<QuestRewards>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestCompleteRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", nameof(QuestCompleteRequest));
            return false;
        }

        int pid = Game.PidOf(client);
        bool acquired = pid != 0 && _questStore.Acquired(pid).Contains(packet.Index);
        if (!acquired)
        {
            client.SendPacket(new QuestComplete
                { Answer = QuestAcquire.NotAcquired, QuestIndex = packet.Index });
            _logger.LogWarning("[Quests] rejected completion of unacquired quest {0}, PID {1}.", packet.Index, pid);
            return true;
        }
        if (!_questStore.ReadyToComplete(pid, packet.Index))
        {
            client.SendPacket(new QuestComplete
                { Answer = QuestAcquire.WrongObjective, QuestIndex = packet.Index });
            _logger.LogWarning("[Quests] rejected premature completion of quest {0}, PID {1}; " +
                "the authoritative instance has not reported QUESTCOMPLETE_REWARD.", packet.Index, pid);
            return true;
        }

        // Modifier_Give objects exported under this quest's species table.
        var reward = _questRewards.Get(pid, packet.Index);
        if (!QuestRewards.TrySelectOptional(reward, packet.ItemSlot))
        {
            client.SendPacket(new QuestComplete
                { Answer = QuestAcquire.DontSelectItem, QuestIndex = packet.Index });
            _logger.LogWarning("[Quests] rejected invalid optional reward slot {0}/{1} for quest {2}, PID {3}.",
                packet.ItemSlot, reward.OptionalItems.Count, packet.Index, pid);
            return true;
        }
        var mission = _questStore.GetProgress(pid, packet.Index);
        var completion = CompleteInTransaction(pid, packet.Index, reward, mission);
        if (completion.Rejection is { } rejection)
        {
            client.SendPacket(new QuestComplete { Answer = rejection, QuestIndex = packet.Index });
            _logger.LogWarning("[Quests] rejected duplicate or stale completion of quest {0}, PID {1}: {2}.",
                packet.Index, pid, rejection);
            return true;
        }
        if (completion.Progress is not { } progress)
        {
            client.SendPacket(new QuestComplete
                { Answer = QuestAcquire.InventoryFull, QuestIndex = packet.Index });
            _logger.LogWarning("[Quests] rejected completion of quest {0}, PID {1}: MAIN inventory cannot hold {2} reward item(s).",
                packet.Index, pid, reward.Items.Count);
            return true;
        }

        if (_questRoomMembers.QuestOf(client.Id) == packet.Index)
        {
            _cityManager.LeaveCurrentPresence(client, pid);
            _questRoomMembers.ClearQuest(client.Id);
            _questInstanceManager.LeaveQuestInstance(client.Id, pid);
        }

        client.SendPacket(new QuestComplete { Answer = QuestResultCode.Ok, QuestIndex = packet.Index });
        QuestManager.SendQuestInventoryChanges(client, completion.RewardCells, completion.RewardItems);
        QuestManager.SendProgressParameters(client, progress);
        _characterManager.SendLevelProgressionEvents(client, pid, completion.PreviousLevel, progress.Level);
        _questManager.SendQuestAvailabilityUpdate(client, pid);
        _logger.LogInformation(
            "[Quests] PID {0} completed {1} (reward slot {2}, {3} kills): +{4} BP, +{5} QT, +{6} LP, +{7} money, {8} item reward(s). Now BP {9}, QT {10}, LP {11}, Money {12}.",
            pid, packet.Index, packet.ItemSlot, mission.KilledMonsters, reward.BP + completion.Paid.BP, completion.Paid.QT,
            reward.LicensePoints, reward.Money, reward.Items.Count, progress.BP, progress.QT,
            progress.LP, progress.Money);
        return true;
    }

    private sealed record Completion(byte? Rejection, PlayerStore.PlayerProgress? Progress, int PreviousLevel,
        List<PlayerStore.GenericItem> RewardItems, List<byte> RewardCells, (int BP, int QT) Paid);

    // Takes the quest's removal items, gives the reward items and progression, and completes the
    // quest in one transaction. Progress is null when MAIN cannot hold the reward items.
    private static Completion CompleteInTransaction(int pid, int questIndex, QuestRewards.Reward reward,
        QuestStore.Progress mission)
    {
        var questData = _questCatalog.Get(questIndex);
        List<PlayerStore.GenericItem> rewardItems = [];
        List<byte> rewardCells = [];
        PlayerStore.PlayerProgress? progress = null;
        int previousLevel = 0;
        byte? rejection = null;
        (int BP, int QT) paid = (0, 0);
        _multiStoreTransaction.Run(() =>
        {
            // The UI can resend op45 before the first answer; recheck inside the transaction so
            // only one request consumes items and rewards.
            bool stillAcquired = _questStore.Acquired(pid).Contains(questIndex);
            if (!stillAcquired || !_questStore.ReadyToComplete(pid, questIndex))
            {
                rejection = stillAcquired ? QuestAcquire.WrongObjective : QuestAcquire.NotAcquired;
                return;
            }
            if (!_playerStore.TryExchangeQuestItems(pid, questData?.RemoveItems ?? [],
                    reward.Items.Select(i => (i.Item, i.Count)), out rewardItems, out rewardCells))
                return;
            if (!_questStore.Complete(pid, questIndex, questData?.Repeatable == true))
            {
                rejection = QuestAcquire.NotAcquired;
                return;
            }
            _itemManager.SynchronizeQuestPickupObjectives(pid,
                (questData?.RemoveItems ?? []).Concat(reward.Items.Select(item => item.Item)));
            // The stored running total is paid against the run and hourly allowances like an op54
            // settlement (QuestInstanceManager.ClampStoredRunReward). The table reward is paid
            // separately below, so it adds nothing to that allowance.
            paid = _questInstanceManager.ClampStoredRunReward(mission.RunInstance, mission.RunStarted, pid,
                questIndex, mission.BP, mission.QT, questRewardBp: 0);
            progress = _playerStore.AddReward(pid, 0, reward.BP + paid.BP, reward.Money,
                reward.LicensePoints, paid.QT, out previousLevel);
        });
        return new Completion(rejection, progress, previousLevel, rewardItems, rewardCells, paid);
    }
}
