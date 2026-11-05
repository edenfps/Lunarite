using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Lunarite.Network;

namespace Lunarite.World;

// Every character that has been in the world since startup, and the Game link each selected
// character is on.
public sealed class PlayerManager
{

    private readonly ConcurrentDictionary<int, Player> _players = [];
    private readonly ConcurrentDictionary<int, Client> _online = [];
    private readonly ConcurrentDictionary<int, Client> _links = [];

    public Player Get(int pid) => _players.GetOrAdd(pid, static pid => new Player(pid));

    public IEnumerable<KeyValuePair<int, Client>> Online => _online;

    public ICollection<int> OnlinePids => _online.Keys;

    public ICollection<Client> OnlineClients => _online.Values;

    public KeyValuePair<int, Client>[] OnlineSnapshot() => _online.ToArray();

    public bool IsOnline(int pid) => _online.ContainsKey(pid);

    public bool TryGetClient(int pid, [NotNullWhen(true)] out Client? client) =>
        _online.TryGetValue(pid, out client);

    public Client GetClient(int pid) => _online[pid];

    public Client? FindClient(int pid) => _online.GetValueOrDefault(pid);

    public void SetOnline(int pid, Client client) => _online[pid] = client;

    public void SetOffline(int pid) => _online.TryRemove(pid, out _);

    // Game links that passed the Agent handoff, selected or not.
    public ICollection<Client> Links => _links.Values;

    public void AddLink(Client client) => _links[client.Id] = client;

    public void RemoveLink(Client client) => _links.TryRemove(client.Id, out _);
}
