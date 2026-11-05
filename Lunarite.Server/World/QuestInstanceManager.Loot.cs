using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed partial class QuestInstanceManager
{
    // Field-mission checkpoint/abort settlement: HuxleyFieldQuestGame.CheckRallyPoint and
    // QuestAbort send rally 0/0, QUESTCOMPLETE_UNKNOWN with the BP/QT earned since the last
    // settlement, then the client banks and zeroes LevelBP (StockTotalBP) and advances
    // AcquiredPCr. Each report is an increment; rally reports (index != 0) carry a running total
    // and are not credited.
    public static bool IsDirectFieldSettlement(bool telemetryValid, bool bpValid, byte rallyPoint,
        byte maxRallyPoint, byte complete, int bp, int qt) =>
        telemetryValid && bpValid && QuestStore.IsSettlementReport(rallyPoint, maxRallyPoint, complete)
        && (bp != 0 || qt != 0);

    public bool IsQuestLootRequestAuthorized(int pid, int questIndex, int instanceToken)
    {
        if (pid <= 0 || questIndex <= 0 || instanceToken <= 0) return false;
        if (_questStore.Acquired(pid).Contains(questIndex)) return true;
        // Scout/field missions can be entered without acquiring them. Entry conditions were
        // checked when the instance token was issued and are not re-evaluated per drop.
        return string.Equals(_questCatalog.Get(questIndex)?.QuestType,
            "QuestType_Free", StringComparison.Ordinal);
    }

    public static int ResolveQuestReportPid(int selectedPid, int wirePid, int activeQuest,
        int instanceToken, int participantPid, int participantQuest, int participantInstance,
        bool participantAuthorized)
    {
        if (selectedPid <= 0 || activeQuest <= 0 || instanceToken <= 0) return 0;
        if (wirePid == selectedPid || wirePid == activeQuest) return selectedPid;
        return wirePid == participantPid && participantPid > 0 && participantAuthorized
            && participantQuest == activeQuest && participantInstance == instanceToken
            ? participantPid : 0;
    }

    public static int ResolveQuestPickupOwner(
        int selectedPid, int activeQuest, int instanceToken, int instanceHostPid) =>
        selectedPid <= 0 || activeQuest <= 0 || instanceToken <= 0 ? 0
        : selectedPid == instanceHostPid ? activeQuest : selectedPid;

    // Retail loot is per player. Kill (op56) and crate (op57) requests come only from the listen
    // host, so each one is rolled once per participant and sent back as one op59/op60 per roll,
    // carrying that participant's OwnerID: HuxleyQuestPickup.IsOwner shows a pickup only to its
    // owner, and the native handler drops every op59 at the enemy that armed Delegate_DropItem.
    // The host's own wire identity is the quest index (ResolveQuestPickupOwner).
    internal List<int> LootRecipients(int instanceToken, int reporterPid)
    {
        var pids = new List<int>();
        if (reporterPid > 0) pids.Add(reporterPid);
        if (instanceToken <= 0) return pids;
        foreach (var member in _questRoomMembers.InstanceMembers())
        {
            if (member.Value != instanceToken) continue;
            var memberClient = _playerManager.OnlineClients.FirstOrDefault(x => x.Id == member.Key);
            int pid = memberClient != null ? Game.PidOf(memberClient)
                : _questRoomMembers.DetachedPids.FirstOrDefault(x => _questRoomMembers.DetachedClientOf(x) == member.Key);
            if (pid > 0 && !pids.Contains(pid)) pids.Add(pid);
        }
        return pids;
    }

    // op47/op48 come from the mission-end screen, which can arrive after the player has left the
    // instance: when the host leaves, its guests are sent back to the city at once
    // (ReturnQuestMembersAfterHostExit) and their pickup lists land a moment later. Each leaver
    // keeps its run's pool for LootClaimGrace; the pool itself is cleared once that has passed.
    internal static readonly TimeSpan LootClaimGrace = TimeSpan.FromMinutes(2);
    private readonly record struct LootClaimWindow(int Instance, int PoolPid, long RunStarted, long ExpiresTicks);
    private readonly ConcurrentDictionary<int, LootClaimWindow> _lootClaimWindows = new();

    internal void OpenLootClaimWindows(int pid, int instanceToken, long? nowTicks = null)
    {
        if (pid <= 0 || instanceToken <= 0) return;
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        long run = StartedTicks(instanceToken);
        _lootClaimWindows[pid] = new LootClaimWindow(instanceToken, pid, run, now + LootClaimGrace.Ticks);
        _ = System.Threading.Tasks.Task.Delay(LootClaimGrace).ContinueWith(_ =>
        {
            if (StartedTicks(instanceToken) == run) QuestLootGrants.ClearParticipant(instanceToken, pid);
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    // The instance and pool an op47/op48 pickup claim draws from: the current run, or the run the
    // player left within LootClaimGrace (as long as its host has not started a new one).
    internal bool TryResolveLootClaim(int clientId, int pid, out int instanceToken, out int poolPid,
        long? nowTicks = null)
    {
        instanceToken = _questRoomMembers.InstanceOf(clientId);
        if (IsQuestLootRequestAuthorized(pid, _questRoomMembers.QuestOf(clientId), instanceToken))
        {
            poolPid = pid;
            return true;
        }
        long now = nowTicks ?? DateTime.UtcNow.Ticks;
        if (_lootClaimWindows.TryGetValue(pid, out var window) && window.ExpiresTicks > now
            && StartedTicks(window.Instance) == window.RunStarted)
        {
            instanceToken = window.Instance;
            poolPid = window.PoolPid;
            return true;
        }
        poolPid = 0;
        return false;
    }

    // DEBUG_LOOT: op59 with item 604221 every 3s, 20 times.
    private void StartLootProbe(Client client)
    {
        _logger.LogWarning("[LootProbe] DEBUG_LOOT on: will send op59 every 3s to test if Delegate_DropItem is armed.");
        System.Threading.Tasks.Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                await System.Threading.Tasks.Task.Delay(3000);
                var drop = new QuestListenDropItem { Opcode = 59, Pid = 0 };
                drop.Drops.Add(new QuestListenDropItem.Drop(604221, 1));
                client.SendPacket(drop);
                _logger.LogWarning("[LootProbe] sent op59 #{0} (item 604221)", i + 1);
            }
        });
    }

    // Listen packets identify the room by QuestCreate fixed block #3 (the quest index), not the
    // character PID. Maps that wire identity to the owning character.
    internal int ResolveQuestReportOwner(Client client, int wirePid, int? wireQuest = null)
    {
        int selectedPid = Game.PidOf(client);
        if (selectedPid <= 0) return 0;
        int activeQuest = _questRoomMembers.QuestOf(client.Id);
        int activeInstance = _questRoomMembers.InstanceOf(client.Id);
        bool reporterAuthorized = !wireQuest.HasValue
            || (wireQuest.Value == activeQuest
                && IsQuestLootRequestAuthorized(selectedPid, activeQuest, activeInstance));
        if (!reporterAuthorized) return 0;

        int participantPid = 0;
        int participantQuest = 0;
        int participantInstance = 0;
        bool participantAuthorized = false;
        if (wirePid != selectedPid && wirePid != activeQuest)
        {
            // A traveling guest's binding is kept under its old client ID, so the host can still
            // report its rally/objective/result.
            int participantClientId = _questManager.QuestParticipantClientId(wirePid);
            if (participantClientId > 0)
            {
                participantPid = wirePid;
                participantQuest = _questRoomMembers.QuestOf(participantClientId);
                participantInstance = _questRoomMembers.InstanceOf(participantClientId);
                participantAuthorized = !wireQuest.HasValue || (wireQuest.Value == participantQuest
                    && IsQuestLootRequestAuthorized(participantPid, participantQuest, participantInstance));
            }
        }
        // The listen server sends SendQuestListenRallyPoint for every remote controller over the
        // host's link. Records go to the named participant only if both share the instance.
        return ResolveQuestReportPid(selectedPid, wirePid, activeQuest, activeInstance,
            participantPid, participantQuest, participantInstance, participantAuthorized);
    }
}
