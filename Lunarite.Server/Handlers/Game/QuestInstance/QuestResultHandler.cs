using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Resources;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// SendQuestResult is per-player mission telemetry, not an item list. The first fixed block
// has four weapon and five armor durability triples.
[PacketHandler]
public static class QuestResultHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;
    private static BattleResultStore _battleResultStore = null!;
    private static PlayerStore _playerStore = null!;
    private static QuestStore _questStore = null!;
    private static QuestCatalog _questCatalog = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestResultHandler));

        _battleResultStore = serviceProvider.GetRequiredService<BattleResultStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
        _questCatalog = serviceProvider.GetRequiredService<QuestCatalog>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestResultRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("op60 SendQuestResult: malformed payload.");
            return false;
        }

        int selectedPid = Game.PidOf(client);
        int questIndex = _questRoomMembers.QuestOf(client.Id);
        // Only HuxleyQuestGame sends this, at a rally/end transition, so it requires an active room.
        int ownerPid = questIndex > 0 ? _questInstanceManager.ResolveQuestReportOwner(client, packet.Pid, questIndex) : 0;
        if (ownerPid <= 0)
        {
            _logger.LogWarning("op60 SendQuestResult rejected: PID={0}, selected={1}.", packet.Pid, selectedPid);
            return true;
        }

        int instanceHost = _questRoomMembers.InstanceOf(client.Id);
        bool nativeBackup = Environment.GetEnvironmentVariable("QUEST_P2P_BACKUP_PATCH") == "1";
        var equipment = ownerPid == selectedPid ? packet.Equipment
            : ClampOtherParticipantWear(instanceHost, ownerPid, questIndex, packet.Equipment, bootsOnly: !nativeBackup);
        int updated;
        if (nativeBackup)
            updated = _playerStore.UpdateNativeQuestDurability(ownerPid,
                equipment.Select(x => (x.Inventory, x.Cell, x.Durability)));
        else
        {
            var boots = equipment.FirstOrDefault(x => x.Inventory == InventoryType.Armor && x.Cell == 4);
            updated = boots.Inventory == InventoryType.Armor
                ? _playerStore.UpdateQuestDurabilityFromBoots(ownerPid, boots.Durability)
                : 0;
        }
        if (updated != 0) QuestManager.QuestRewardLiveRecipient(ownerPid)?.Session.QueueEquipmentRefresh();
        bool recordedWeapons = _questStore.UpdateWeaponKills(ownerPid, questIndex,
            packet.WeaponUses.Select(x => (x.Category, x.Kills)), authenticatedDirectFieldRun: true);
        long runStarted = _questInstanceManager.StartedTicks(instanceHost);
        _battleResultStore.SetQuestRun(ownerPid, $"{questIndex}:{instanceHost}:{runStarted}",
            packet.WeaponUseData, packet.Summary, packet.Detail,
            runStarted > 0 ? (int)TimeSpan.FromTicks(DateTime.UtcNow.Ticks - runStarted).TotalSeconds : 0);
        _logger.LogInformation("[Quests] op60 result PID {0}: updated {1} equipped item(s); " +
            "weapon telemetry recorded={2}; summary/detail 48/104 bytes.", ownerPid, updated, recordedWeapons);
        return true;
    }

    // The host reports its guests' durability too. Wear it applies to another participant is
    // limited per result and per run (QuestInstanceManager.ClampOtherParticipantWear); each
    // reported value is raised to the stored durability minus the allowed wear. Without the
    // native backup patch only the boots slot is a genuine value (UpdateQuestDurabilityFromBoots).
    private static List<QuestResultRequest.EquipmentDurability> ClampOtherParticipantWear(int instanceHost,
        int ownerPid, int questIndex, IReadOnlyList<QuestResultRequest.EquipmentDurability> reported, bool bootsOnly)
    {
        var stored = _playerStore.GetInventory(ownerPid, () => [])
            .Where(item => item.InventoryIndex is InventoryType.Weapon or InventoryType.Armor)
            .GroupBy(item => (item.InventoryIndex, item.InventoryCell))
            .ToDictionary(group => group.Key, group => (int)group.First().Durability);
        // An unused weapon reports zero, which UpdateNativeQuestDurability ignores.
        bool Genuine(QuestResultRequest.EquipmentDurability x) => bootsOnly
            ? x.Inventory == InventoryType.Armor && x.Cell == 4
            : x.Inventory != InventoryType.Weapon || x.Durability > 0;
        int wear = reported.Where(Genuine)
            .Select(x => stored.TryGetValue((x.Inventory, x.Cell), out int current)
            ? current - x.Durability : 0).DefaultIfEmpty(0).Max();
        int allowed = _questInstanceManager.ClampOtherParticipantWear(instanceHost, ownerPid, wear,
            _questCatalog.Get(questIndex)?.DurabilityDecParam ?? 9);
        return reported.Select(x => Genuine(x) && stored.TryGetValue((x.Inventory, x.Cell), out int current)
                && current - x.Durability > allowed
                ? x with { Durability = (byte)Math.Max(0, current - allowed) } : x).ToList();
    }
}
