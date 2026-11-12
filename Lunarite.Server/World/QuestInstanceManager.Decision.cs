using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

public sealed partial class QuestInstanceManager
{
    // Party decisions the members accepted, by host PID, until the room is created.
    private readonly ConcurrentDictionary<int, PendingQuestDecision> _acceptedQuestGroups = [];

    internal sealed class PendingQuestDecision
    {
        public int QuestIndex;
        public byte Difficulty;
        public int LeaderPid;
        public List<int> Members = [];
        public Dictionary<int, byte> Answers = [];
        public bool Finalized;
    }

    public static int SelectQuestHostPid(int leaderPid,
        IReadOnlyCollection<(int Pid, System.Net.IPAddress Address)> participants)
    {
        if (participants.Count < 2 || participants.Any(x => x.Pid <= 0)
            || !participants.Any(x => x.Pid == leaderPid))
            return leaderPid;

        // Same-PC test heuristic: the launcher gives the first process 127.0.0.2. A later
        // process's P2P UDP init can move from 7777 to 7778, so the lowest loopback address hosts.
        if (!participants.All(x => System.Net.IPAddress.IsLoopback(x.Address.MapToIPv4())))
            return leaderPid;
        return participants
            .OrderBy(x => BitConverter.ToUInt32(x.Address.MapToIPv4().GetAddressBytes(), 0))
            .ThenBy(x => x.Pid)
            .First().Pid;
    }

    internal void FinalizePendingQuestDecision(PendingQuestDecision pending)
    {
        foreach (int memberPid in pending.Members)
            if (Game.TryGetClient(memberPid, out var memberClient)
                && _pendingQuestDecisions.TryGetValue(memberClient.Id, out var current)
                && ReferenceEquals(current, pending))
                _pendingQuestDecisions.TryRemove(memberClient.Id, out _);

        var completedAnswers = pending.Members.Select(memberPid => new QuestSquadAnswers.Entry(
            memberPid, pending.QuestIndex, pending.Answers.GetValueOrDefault(memberPid, (byte)1))).ToList();
        if (pending.Members.Count > 1 && Game.TryGetClient(pending.LeaderPid, out var leader))
            leader.SendPacket(new QuestSquadAnswers { Opcode = 221, Answers = completedAnswers });

        if (!completedAnswers.All(x => x.Answer == 0))
        {
            // HuxleyPCMMOGame locks input on consent. OnSquadNotify with
            // SQUAD_NOT_ALL_ENTER closes any remaining consent prompt and unlocks it; the answer
            // list goes only to the leader.
            foreach (int memberPid in pending.Members)
                if (Game.TryGetClient(memberPid, out var memberClient))
                    memberClient.SendPacket(ChatManager.BuildQuestDecisionFailureNotice(pending.LeaderPid));
            return;
        }
        var participants = pending.Members
            .Where(_playerManager.IsOnline)
            .Select(pid => (pid, _playerManager.GetClient(pid).IPAddress))
            .ToList();
        int hostPid = SelectQuestHostPid(pending.LeaderPid, participants);
        if (!Game.TryGetClient(hostPid, out var hostClient)) return;
        if (hostPid != pending.LeaderPid)
            _logger.LogInformation("[Quest] local party host moved from leader PID {0} to canonical-port PID {1}.",
                pending.LeaderPid, hostPid);
        if (pending.Members.Count > 1) _acceptedQuestGroups[hostPid] = pending;
        QuestCreate(hostClient, pending.QuestIndex, pending.Difficulty);
    }

    public static bool ApplyQuestDecisionDisconnect(int disconnectedPid,
        IReadOnlyCollection<int> members, IDictionary<int, byte> answers, ref bool finalized)
    {
        if (finalized || disconnectedPid <= 0 || !members.Contains(disconnectedPid)) return false;
        answers[disconnectedPid] = QuestAcquire.NotFoundPlayer;
        finalized = true;
        return true;
    }

    internal void AbortPendingQuestDecisionOnDisconnect(int clientId, int pid)
    {
        if (!_pendingQuestDecisions.TryRemove(clientId, out var pending)) return;
        bool abort;
        lock (pending)
            abort = ApplyQuestDecisionDisconnect(pid, pending.Members, pending.Answers,
                ref pending.Finalized);
        if (abort)
        {
            _logger.LogWarning("[Quest] squad decision aborted because PID {0} disconnected.", pid);
            FinalizePendingQuestDecision(pending);
        }
    }

    // One squad member's answer to the leader's gate check: online, in the leader's city zone,
    // allowed to enter, equipped, and carrying the entry items.
    internal byte PreflightQuestMember(int memberPid, int leaderPid, int questIndex, QuestCatalog.Quest? quest)
    {
        if (!_playerManager.IsOnline(memberPid)) return QuestResultCode.NotfoundPlayer;
        if (memberPid != leaderPid && (Game.PlayerFor(memberPid).CityMap ?? 0) != (Game.PlayerFor(leaderPid).CityMap ?? 0))
            return QuestResultCode.FarFromOwner;
        // A member who finished the quest, or never took it, comes along as a helper (decided 2026-11-10): kill BP and
        // loot, no quest progress. Only the leader must be able to run it.
        if (memberPid == leaderPid && !_questManager.CanEnterQuest(memberPid, questIndex))
            return _questManager.QuestUnavailableAnswer(memberPid, questIndex);
        byte answer = ValidateQuestEquipment(_playerStore.GetInventory(memberPid, () => []));
        if (answer == 0 && quest != null && !_playerStore.HasQuestEntryItems(memberPid,
                quest.EnterRequestItems.Select(x => (x.ItemIndex, x.ItemCount))))
            answer = QuestResultCode.DonthaveEnterItem;
        return answer;
    }

    // The leader's own QuestCheck is its consent; the other members get the op221 prompt
    // (QuestEnterRequestDlgMessage) and silent members are refused after the timeout.
    internal void StartQuestDecision(int pid, List<int> members, int questIndex, byte difficultyLevel)
    {
        var pending = new PendingQuestDecision
        {
            QuestIndex = questIndex,
            Difficulty = difficultyLevel,
            LeaderPid = pid,
            Members = members
        };
        // The requester's own QuestCheck is its consent. The op221 prompt
        // (QuestEnterRequestDlgMessage: "enter the quest with your squad leader?") is only for the
        // other squad members; a solo player goes straight to room creation.
        pending.Answers[pid] = 0;
        if (QuestDecisionRecipients(pid, members).Count == 0)
        {
            pending.Finalized = true;
            FinalizePendingQuestDecision(pending);
            return;
        }
        // Register the decision for every member before sending any prompt, so a fast answer
        // cannot arrive before its pending state exists or finish the group early.
        var decisionClients = QuestDecisionRecipients(pid, members)
            .Select(memberPid => Game.TryGetClient(memberPid, out var memberClient) ? memberClient : null)
            .Where(memberClient => memberClient != null)
            .ToList();
        foreach (var memberClient in decisionClients)
            _pendingQuestDecisions[memberClient!.Id] = pending;
        foreach (var memberClient in decisionClients)
            memberClient!.SendPacket(new QuestDecision
            {
                Pid = pid,
                QuestIndex = questIndex,
                DifficultyLevel = difficultyLevel
            });
        int timeoutSeconds = int.TryParse(
                Environment.GetEnvironmentVariable("QUEST_DECISION_TIMEOUT_SECONDS"), out int configured)
            ? Math.Clamp(configured, 5, 300) : 30;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            bool timedOut = false;
            lock (pending)
            {
                if (!pending.Finalized)
                {
                    pending.Finalized = true;
                    foreach (int memberPid in pending.Members)
                        pending.Answers.TryAdd(memberPid, 1); // ANSWER_REFUSE for silent/time-out members
                    timedOut = true;
                }
            }
            if (timedOut)
            {
                _logger.LogWarning(
                    "[Quest] squad decision timed out for quest {0}; unresolved members refused.", pending.QuestIndex);
                FinalizePendingQuestDecision(pending);
            }
        });
    }

    public static IReadOnlyList<int> QuestDecisionRecipients(int requesterPid, IEnumerable<int> members) =>
        members.Where(memberPid => memberPid != requesterPid).Distinct().ToList();

    // Exported ENUM_RESULTTYPE_QUEST: 9=DEFICIENCY, 10=EXCEED. Bounds come from the quest table;
    // checked at the gate and again at creation.
    public static byte ValidateQuestPartySize(QuestCatalog.Quest? quest, int playerCount)
    {
        if (quest == null || playerCount <= 0) return 1;
        if (playerCount < quest.MinPlayers) return 9;
        if (playerCount > quest.MaxPlayers) return 10;
        return 0;
    }

    // Order from the exported quest results and INT localization:
    // 11=no armor, 12=no weapon, 13=an equipped item has zero durability.
    public static byte ValidateQuestEquipment(IEnumerable<PlayerStore.GenericItem> inventory)
    {
        var equipped = inventory.Where(x => x.InventoryIndex is InventoryType.Weapon or InventoryType.Armor).ToList();
        if (!equipped.Any(x => x.InventoryIndex == InventoryType.Armor)) return 11;
        if (!equipped.Any(x => x.InventoryIndex == InventoryType.Weapon)) return 12;
        if (equipped.Any(x => x.Durability <= 0)) return 13;
        return 0;
    }

    public static bool IsQuestStartAuthorized(int requestedQuest, int activeQuest, int activeInstance) =>
        requestedQuest > 0 && requestedQuest == activeQuest && activeInstance > 0;

    // Native op40 SendQuestEnter(nKey) is only sent by HuxleyMMOSubsystem.OnQuestEnterRequest, i.e.
    // as the reply to server op44 (key, quest). A key is valid once, for the connection and quest
    // it was issued to; an unsolicited or replayed op40 is refused.

    public static void IssueQuestEnterRequest(Client client, int questIndex)
    {
        int key = Random.Shared.Next(1, int.MaxValue);
        client.Session.SetQuestEnterKey(key, questIndex);
        client.SendPacket(new QuestEnterRequestNotice { Key = key, QuestIndex = questIndex });
    }

    internal void ForgetAcceptedGroup(int hostPid) => _acceptedQuestGroups.TryRemove(hostPid, out _);
}
