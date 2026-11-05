using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class QuestAcquireHandler
{
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static ItemManager _itemManager = null!;
    private static QuestCatalog _questCatalog = null!;
    private static QuestManager _questManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestConditions _questConditions = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestAcquireHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questConditions = serviceProvider.GetRequiredService<QuestConditions>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestIndexRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize {0}.", "QuestAcquire");
            return false;
        }

        _logger.LogDebug("Received {0} packet. ( QuestIndex={1} )", "QuestAcquire", packet.QuestIndex);

        int pid = Game.PidOf(client);
        var quest = _questCatalog.Get(packet.QuestIndex);
        byte answer = pid <= 0 ? QuestAcquire.Error
            : quest == null ? QuestAcquire.NotFound
            : _questStore.Acquired(pid).Contains(packet.QuestIndex) ? QuestAcquire.AlreadyAcquired
            : (!quest.Repeatable && _questStore.Completed(pid).Contains(packet.QuestIndex))
                ? QuestAcquire.AlreadyComplete
            : !_questConditions.Available(pid).Contains(packet.QuestIndex) ? QuestAcquire.Impossible
            : QuestAcquire.Ok;
        if (answer != QuestAcquire.Ok)
        {
            _logger.LogWarning("[Quests] rejected acquire for quest {0} / PID {1}: result {2}.",
                packet.QuestIndex, pid, answer);
            client.SendPacket(new QuestAcquire { Answer = answer, QuestIndex = packet.QuestIndex });
            _questManager.FinishQuestShareAcquire(client, pid, packet.QuestIndex, answer);
            return true;
        }
        bool acquired = false;
        List<PlayerStore.GenericItem> questItems = [];
        List<byte> questItemCells = [];
        _multiStoreTransaction.Run(() =>
        {
            if (!_questStore.Acquire(pid, packet.QuestIndex)) return;
            if (!_playerStore.TryExchangeQuestItems(pid, [], quest!.QuestItems.Select(x => (x, 1)),
                    out questItems, out questItemCells))
            {
                _questStore.Retire(pid, packet.QuestIndex);
                return;
            }
            _itemManager.InitializeQuestInventoryObjectives(pid, packet.QuestIndex);
            acquired = true;
        });
        if (!acquired && _questStore.Acquired(pid).Contains(packet.QuestIndex))
        {
            client.SendPacket(new QuestAcquire
                { Answer = QuestAcquire.AlreadyAcquired, QuestIndex = packet.QuestIndex });
            _questManager.FinishQuestShareAcquire(client, pid, packet.QuestIndex, QuestAcquire.AlreadyAcquired);
            return true;
        }
        if (!acquired)
        {
            client.SendPacket(new QuestAcquire
                { Answer = QuestAcquire.InventoryFull, QuestIndex = packet.QuestIndex });
            _questManager.FinishQuestShareAcquire(client, pid, packet.QuestIndex, QuestAcquire.InventoryFull);
            return true;
        }
        // Acquire has its own op37 callback; op38 is the full journal list and does not fire
        // Delegate_QuestAcquire.
        client.SendPacket(new QuestAcquire { Answer = QuestAcquire.Ok, QuestIndex = packet.QuestIndex });
        _questManager.FinishQuestShareAcquire(client, pid, packet.QuestIndex, QuestAcquire.Ok);
        QuestManager.SendQuestInventoryChanges(client, questItemCells, questItems);
        _questManager.SendQuestAvailabilityUpdate(client, pid);

        // No QuestDecision here: it would fire HuxleyPCMMOGame.OnQuestEnterRequest and an
        // immediate enter prompt. Entry starts at the quest gate (SafeSendQuestCheck -> op33).

        return true;
    }
}
