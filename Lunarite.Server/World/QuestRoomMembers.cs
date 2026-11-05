using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Lunarite.World;

// Which mission each Game link is in: its quest, its instance (the host PID) and difficulty.
// Entries are keyed by Client.Id. A link that drops for quest travel keeps its entries and is
// remembered as the character's detached link until the character comes back or the room closes.
public sealed class QuestRoomMembers
{
    private readonly ConcurrentDictionary<int, int> _quests = new();
    private readonly ConcurrentDictionary<int, int> _instances = new();
    private readonly ConcurrentDictionary<int, byte> _difficulties = new();
    private readonly ConcurrentDictionary<int, int> _detached = new(); // PID -> old Client.Id

    public int QuestOf(int clientId) => _quests.GetValueOrDefault(clientId);

    public int InstanceOf(int clientId) => _instances.GetValueOrDefault(clientId);

    public byte DifficultyOf(int clientId) => _difficulties.GetValueOrDefault(clientId);

    public void SetQuest(int clientId, int questIndex) => _quests[clientId] = questIndex;

    public void SetInstance(int clientId, int instance) => _instances[clientId] = instance;

    public void SetDifficulty(int clientId, byte difficulty) => _difficulties[clientId] = difficulty;

    public void ClearQuest(int clientId) => _quests.TryRemove(clientId, out _);

    public void ClearDifficulty(int clientId) => _difficulties.TryRemove(clientId, out _);

    public bool TryClearInstance(int clientId, out int instance) => _instances.TryRemove(clientId, out instance);

    // Links currently in the instance.
    public int MemberCount(int instance) => _instances.Values.Count(x => x == instance);

    public KeyValuePair<int, int>[] InstanceMembers() => _instances.ToArray();

    public ICollection<int> DetachedPids => _detached.Keys;

    public bool IsDetached(int pid) => _detached.ContainsKey(pid);

    public int DetachedClientOf(int pid) => _detached.GetValueOrDefault(pid);

    public void Detach(int pid, int clientId) => _detached[pid] = clientId;

    public bool TryTakeDetached(int pid, out int clientId) => _detached.TryRemove(pid, out clientId);
}
