using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// US-client binary: SendQuestRetire -> opcode 44, format "d" (quest id).
// Server opcode 48 invokes OnQuestRetire(byte answer, int questIndex).
[PacketHandler]
public static class QuestRetireHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static MultiStoreTransaction _multiStoreTransaction = null!;
    private static QuestCatalog _questCatalog = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ItemManager _itemManager = null!;
    private static QuestManager _questManager = null!;
    private static ILogger _logger = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestRetireHandler));

        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _itemManager = serviceProvider.GetRequiredService<ItemManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
        _multiStoreTransaction = serviceProvider.GetRequiredService<MultiStoreTransaction>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestIndexRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("Failed to deserialize QuestRetire.");
            return false;
        }

        int pid = Game.PidOf(client);
        bool retired = false;
        List<byte> changedCells = [];
        var removeItems = _questCatalog.Get(packet.QuestIndex)?.RemoveItems ?? [];
        if (pid != 0)
            _multiStoreTransaction.Run(() =>
            {
                if (!_questStore.Retire(pid, packet.QuestIndex)) return;
                retired = true;
                _playerStore.TryExchangeQuestItems(pid, removeItems, [], out _, out changedCells);
                _itemManager.SynchronizeQuestPickupObjectives(pid, removeItems);
            });
        if (retired && changedCells.Count != 0)
        {
            QuestManager.SendQuestInventoryChanges(client, changedCells, []);
        }
        if (retired && _questRoomMembers.QuestOf(client.Id) == packet.QuestIndex)
        {
            _questRoomMembers.ClearQuest(client.Id);
            _questInstanceManager.LeaveQuestInstance(client.Id, pid);
        }

        client.SendPacket(new QuestRetire
        {
            Answer = retired ? QuestAcquire.Ok : QuestAcquire.NotAcquired,
            QuestIndex = packet.QuestIndex
        });
        if (retired) _questManager.SendQuestAvailabilityUpdate(client, pid);

        if (!retired)
            _logger.LogWarning("[Quests] rejected retirement of unacquired quest {0}, PID {1}.", packet.QuestIndex, pid);
        return true;
    }
}
