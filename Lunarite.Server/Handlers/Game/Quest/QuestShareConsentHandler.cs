using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestShareConsentHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static QuestCatalog _questCatalog = null!;
    private static QuestManager _questManager = null!;
    private static ILogger _logger = null!;
    private static PlayerManager _playerManager = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestConditions _questConditions = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestShareConsentHandler));

        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questConditions = serviceProvider.GetRequiredService<QuestConditions>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int sharerPid)
            || !reader.TryRead(out int questIndex)
            || reader.RemainingLength != 0)
            return false;

        int recipientPid = Game.PidOf(client);
        if (recipientPid <= 0
            || !_questManager.TryTakePendingShare(client.Id, out var pending)
            || pending.SharerPid != sharerPid
            || pending.QuestIndex != questIndex)
        {
            _logger.LogWarning("[Quests] ignored stale/mismatched share consent from PID {0}: sharer={1}, quest={2}.",
                recipientPid, sharerPid, questIndex);
            return true;
        }

        byte answer;
        var quest = _questCatalog.Get(questIndex);
        if (quest == null || !_playerManager.IsOnline(sharerPid)
            || !_questStore.Acquired(sharerPid).Contains(questIndex))
            answer = QuestResultCode.Notacquire;
        else if (_questStore.Completed(recipientPid).Contains(questIndex)) answer = QuestResultCode.AlreadyComplete;
        else if (_questStore.Acquired(recipientPid).Contains(questIndex)) answer = QuestResultCode.Alreadyacquire;
        else if (!_questConditions.Available(recipientPid).Contains(questIndex)) answer = QuestResultCode.Impossible;
        else
        {
            bool acquired = false;
            List<PlayerStore.GenericItem> sharedQuestItems = [];
            List<byte> sharedQuestItemCells = [];
            _multiStoreTransaction.Run(() =>
            {
                if (!_questStore.Acquire(recipientPid, questIndex)) return;
                if (!_playerStore.TryExchangeQuestItems(recipientPid, [], quest!.QuestItems.Select(x => (x, 1)),
                        out sharedQuestItems, out sharedQuestItemCells))
                {
                    _questStore.Retire(recipientPid, questIndex);
                    return;
                }
                _itemManager.InitializeQuestInventoryObjectives(recipientPid, questIndex);
                acquired = true;
            });
            answer = acquired ? (byte)0 : QuestAcquire.InventoryFull;
            if (acquired) QuestManager.SendQuestInventoryChanges(client, sharedQuestItemCells, sharedQuestItems);
        }

        if (Game.TryGetClient(sharerPid, out var sharerClient))
            sharerClient.SendPacket(new QuestShare
                { Pid = recipientPid, QuestIndex = questIndex, Answer = answer });

        client.SendPacket(new QuestShare
            { Pid = sharerPid, QuestIndex = questIndex, Answer = answer });
        if (answer == 0)
        {
            client.SendPacket(new QuestAcquire
                { Answer = QuestAcquire.Ok, QuestIndex = questIndex });
            _questManager.SendQuestAvailabilityUpdate(client, recipientPid);
        }
        return true;
    }
}
