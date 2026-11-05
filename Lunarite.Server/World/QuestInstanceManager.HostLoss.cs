using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;

namespace Lunarite.World;

// A mission's listen host crashed. The members it was hosting lose its world and go back to the
// city; once they have spawned there, the server opens the mission again with one of them as the
// new host (op42 + op55 + op52, as a normal room). The map starts from its beginning: the stock
// client's Type-4 op42 parser skips the backup block, so the new host's own run state cannot be
// restored. Entry items are not taken again.
public sealed partial class QuestInstanceManager
{
    private sealed class RehostPlan
    {
        public int QuestIndex;
        public byte Difficulty;
        public int LeaderPid;
        public List<int> Members = [];
        public HashSet<int> WaitingForSpawn = [];
        public bool Launched;
    }

    private readonly object _rehostLock = new();
    private readonly List<RehostPlan> _rehosts = [];

    // Survivors that do not reach the city in time are left out of the new room.
    private static readonly TimeSpan RehostSpawnTimeout = TimeSpan.FromSeconds(60);

    // The host's link closed during the mission. Returns the members to the city and plans the
    // new room.
    internal void QuestHostLost(int departingClientId, int departingPid, int departingInstance)
    {
        var survivors = _questRoomMembers.InstanceMembers()
            .Where(member => member.Value == departingInstance && member.Key != departingClientId)
            .Select(member => _playerManager.OnlineClients.FirstOrDefault(client => client.Id == member.Key))
            .Where(client => client != null && Game.PidOf(client) > 0)
            .Select(client => client!)
            .ToList();
        if (survivors.Count != 0)
        {
            var first = survivors[0];
            var plan = new RehostPlan
            {
                QuestIndex = _questRoomMembers.QuestOf(first.Id),
                Difficulty = _questRoomMembers.DifficultyOf(first.Id),
                Members = survivors.Select(Game.PidOf).ToList(),
            };
            plan.WaitingForSpawn = plan.Members.ToHashSet();
            var (_, leaderPid) = _squadManager.SquadOf(plan.Members[0]);
            plan.LeaderPid = plan.Members.Contains(leaderPid) ? leaderPid : plan.Members[0];
            if (plan.QuestIndex > 0)
            {
                lock (_rehostLock) _rehosts.Add(plan);
                _logger.LogInformation("[Quest] host PID {0} of quest {1} was lost; re-hosting for PIDs [{2}] once they are in the city.",
                    departingPid, plan.QuestIndex, string.Join(",", plan.Members));
                _ = LaunchRehostAfterTimeout(plan);
            }
        }
        ReturnQuestMembersAfterHostExit(departingClientId, departingPid, departingInstance, true);
    }

    // A character spawned in the city.
    internal void OnCitySpawn(int pid)
    {
        RehostPlan? ready = null;
        lock (_rehostLock)
        {
            var plan = _rehosts.FirstOrDefault(x => !x.Launched && x.WaitingForSpawn.Contains(pid));
            if (plan == null) return;
            plan.WaitingForSpawn.Remove(pid);
            if (plan.WaitingForSpawn.Count == 0) ready = TakeRehostLocked(plan);
        }
        if (ready != null) LaunchRehost(ready);
    }

    // A survivor's link closed before the new room opened.
    internal void ForgetRehostMember(int pid)
    {
        RehostPlan? ready = null;
        lock (_rehostLock)
        {
            var plan = _rehosts.FirstOrDefault(x => !x.Launched && x.Members.Contains(pid));
            if (plan == null) return;
            plan.Members.Remove(pid);
            plan.WaitingForSpawn.Remove(pid);
            if (plan.Members.Count == 0) TakeRehostLocked(plan);
            else if (plan.WaitingForSpawn.Count == 0) ready = TakeRehostLocked(plan);
        }
        if (ready != null) LaunchRehost(ready);
    }

    private async Task LaunchRehostAfterTimeout(RehostPlan plan)
    {
        await Task.Delay(RehostSpawnTimeout).ConfigureAwait(false);
        RehostPlan? ready;
        lock (_rehostLock)
        {
            if (plan.Launched) return;
            plan.Members.RemoveAll(plan.WaitingForSpawn.Contains);
            ready = TakeRehostLocked(plan);
        }
        if (ready.Members.Count != 0) LaunchRehost(ready);
    }

    private RehostPlan TakeRehostLocked(RehostPlan plan)
    {
        plan.Launched = true;
        _rehosts.Remove(plan);
        return plan;
    }

    private void LaunchRehost(RehostPlan plan)
    {
        var online = plan.Members.Where(_playerManager.IsOnline).ToList();
        if (online.Count == 0) return;
        var participants = online.Select(pid => (pid, _playerManager.GetClient(pid).IPAddress)).ToList();
        int leaderPid = online.Contains(plan.LeaderPid) ? plan.LeaderPid : online[0];
        int hostPid = SelectQuestHostPid(leaderPid, participants);
        if (!Game.TryGetClient(hostPid, out var hostClient)) return;
        if (online.Count > 1)
            _acceptedQuestGroups[hostPid] = new PendingQuestDecision
            {
                QuestIndex = plan.QuestIndex, Difficulty = plan.Difficulty, LeaderPid = leaderPid,
                Members = online, Answers = online.ToDictionary(pid => pid, _ => (byte)0), Finalized = true
            };
        _logger.LogInformation("[Quest] re-hosting quest {0} on PID {1} for PIDs [{2}].",
            plan.QuestIndex, hostPid, string.Join(",", online));
        QuestCreate(hostClient, plan.QuestIndex, plan.Difficulty, rehost: true);
    }
}
