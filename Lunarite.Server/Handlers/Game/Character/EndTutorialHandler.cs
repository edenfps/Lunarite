using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class EndTutorialHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestCatalog _questCatalog = null!;
    private static QuestManager _questManager = null!;
    private static CityManager _cityManager = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;
    private static CharacterStore _characterStore = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(EndTutorialHandler));

        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _cityManager = serviceProvider.GetRequiredService<CityManager>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int questIndex) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var quest = _questCatalog.Get(questIndex);
        bool selectedQuest = _questRoomMembers.QuestOf(client.Id) == questIndex;
        bool acquired = pid != 0 && _questStore.Acquired(pid).Contains(questIndex);
        if (quest == null || !quest.QuestType.Equals("QuestType_Tutorial", StringComparison.Ordinal)
            || (!selectedQuest && !acquired))
        {
            _logger.LogWarning("[Tutorial] rejected end request PID {0}, quest {1}.", pid, questIndex);
            return true;
        }

        // Tutorial travel can enter an instance without journaling it first.
        if (!acquired && !_questStore.Acquire(pid, questIndex))
        {
            _logger.LogWarning("[Tutorial] failed to journal selected tutorial PID {0}, quest {1}.", pid, questIndex);
            return true;
        }
        if (!_questStore.Complete(pid, questIndex, quest.Repeatable))
        {
            _logger.LogWarning("[Tutorial] failed to complete journaled tutorial PID {0}, quest {1}.", pid, questIndex);
            return true;
        }
        _cityManager.LeaveCurrentPresence(client, pid);
        _questRoomMembers.ClearQuest(client.Id);
        _questInstanceManager.LeaveQuestInstance(client.Id, pid);
        int cityMap = CharacterStore.TrainingCenterMap(_characterStore.Get(pid)?.Index ?? 1);
        Game.PlayerFor(pid).CityMap = cityMap;
        _characterStore.SetCityLocation(pid, cityMap, 0);
        CityManager.ArmExpectedCityTravel(client);
        client.SendPacket(new LoadMap { Index = cityMap, Position = 0 });
        // EndTutorial skips OnQuestComplete's refresh. After the destination Spawn, op38 drops the
        // tutorial from the journal and op36 shows the unlocked city quests. Sending earlier would
        // run NotifyUpdateQuestList against the outgoing map's menus.
        _questManager.QueueQuestTravelRefresh(client, acquire: true, availability: true);
        _logger.LogInformation("[Tutorial] PID {0} completed {1}; returning to city map {2}.", pid, questIndex, cityMap);
        return true;
    }
}
