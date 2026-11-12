using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

using Lunarite.Resources;
using Lunarite.Storage;

namespace Lunarite.World;

// Discord status for the game client. The client's d3dx9_31.dll asks on the server-list ping port
// (UDP, 6 bytes: "LRP1" + the local TCP port of its game link) and passes the JSON it gets back to
// Discord as the activity, so what is shown can change without a client patch. The asker must come
// from that link's address, so a character's status only goes to its own player.
public sealed class DiscordPresence
{
    private const string Website = "https://huxleythedystopia.com";

    private readonly PlayerManager _players;
    private readonly PlayerStore _progress;
    private readonly CharacterStore _characters;
    private readonly QuestRoomMembers _rooms;
    private readonly QuestCatalog _quests;
    private readonly CityZoneCatalog _cities;
    private readonly BattleZoneCatalog _zones;
    private readonly BattleInstanceManager _battles;
    private readonly SimulationManager _simulation;
    private readonly SimulationMapOverrides _mapOverrides;

    private readonly ConcurrentDictionary<int, (string Key, long Start)> _started = new();
    private readonly Lazy<Dictionary<string, string>> _names = new(LoadNames);

    public DiscordPresence(PlayerManager players, PlayerStore progress, CharacterStore characters, QuestRoomMembers rooms,
        QuestCatalog quests, CityZoneCatalog cities, BattleZoneCatalog zones, BattleInstanceManager battles, SimulationManager simulation,
        SimulationMapOverrides mapOverrides)
    {
        _players = players;
        _progress = progress;
        _characters = characters;
        _rooms = rooms;
        _quests = quests;
        _cities = cities;
        _zones = zones;
        _battles = battles;
        _simulation = simulation;
        _mapOverrides = mapOverrides;
    }

    public static bool TryParse(ReadOnlySpan<byte> request, out ushort port)
    {
        port = 0;
        if (request.Length != 6 || !request[..4].SequenceEqual("LRP1"u8)) return false;
        port = (ushort)(request[4] | request[5] << 8);
        return port != 0;
    }

    public byte[]? Reply(byte[] request, IPEndPoint from)
    {
        if (!TryParse(request, out ushort port)) return null;
        var links = new List<(int Pid, int ClientId, IPAddress Address, int Port)>();
        foreach (var (pid, client) in _players.Online)
            links.Add((pid, client.Id, client.IPAddress, client.RemotePort));
        return Choose(links, port, from.Address) is { } link
            ? Encoding.UTF8.GetBytes(Describe(link.Pid, link.ClientId)) : null;
    }

    // The asker knows its own local port, which a home router rewrites, so the port only settles which of several
    // characters behind one address is asking. A lone character on the address is the asker.
    public static (int Pid, int ClientId, IPAddress Address, int Port)? Choose(
        IEnumerable<(int Pid, int ClientId, IPAddress Address, int Port)> links, ushort localPort, IPAddress from)
    {
        var same = links.Where(link => SameHost(link.Address, from)).ToList();
        foreach (var link in same)
            if (link.Port == localPort) return link;
        return same.Count == 1 ? same[0] : null;
    }

    public static bool SameHost(IPAddress a, IPAddress b)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (b.IsIPv4MappedToIPv6) b = b.MapToIPv4();
        return a.Equals(b) || (IPAddress.IsLoopback(a) && IPAddress.IsLoopback(b));
    }

    private string Describe(int pid, int clientId)
    {
        string who = $"Lv {_progress.GetProgress(pid).Level} {(_characters.Get(pid)?.Index >= 10 ? "Alternative" : "Sapiens")}";
        string details, state;

        int quest = _rooms.QuestOf(clientId);
        if (quest != 0)
        {
            int squad = _rooms.MemberCount(_rooms.InstanceOf(clientId));
            details = $"Mission: {Name(_quests.Get(quest)?.QuestName, "Unknown")}";
            state = squad > 1 ? $"{who} - Squad of {squad}" : who;
        }
        else if (_battles.ServerOf(pid) is var server && server != 0)
        {
            details = $"Battlefield: {Name(_zones.GetByServerId(server)?.LevelName, "Unknown")}";
            state = who;
        }
        else if (_simulation.PresenceOf(pid) is { } room)
        {
            details = $"{(room.InMatch ? "Skirmish" : "Skirmish lobby")}: {ModeName(room.GameType)}";
            state = $"{RoomMapName(room.Map)} - {room.Players}/{room.MaxPlayers} players";
        }
        else
        {
            details = $"In {CityName(_players.Get(pid).CityMap is { } map ? _cities.Get(map) : null)}";
            state = who;
        }
        return ActivityJson(details, state, who, StartOf(pid, details));
    }

    // Discord's clock runs from when this activity began, not from each answer.
    private long StartOf(int pid, string key) =>
        _started.AddOrUpdate(pid, _ => (key, DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            (_, old) => old.Key == key ? old : (key, DateTimeOffset.UtcNow.ToUnixTimeSeconds())).Start;

    public static string ActivityJson(string details, string state, string who, long start) => JsonSerializer.Serialize(new
    {
        details = Clip(details),
        state = Clip(state),
        timestamps = new { start },
        assets = new { large_image = "huxley", large_text = Clip(who) },
        buttons = new[] { new { label = "Play Huxley", url = Website } },
    });

    private static string Clip(string text) => text.Length <= 128 ? text : text[..128];

    public static string ModeName(byte gameType) => gameType switch
    {
        1 => "Team Deathmatch",
        2 => "Capture the Radar",
        3 => "Missile Launch War",
        4 => "Lunar Flag Capture",
        5 => "Deathmatch",
        8 => "Team Hunting",
        9 => "Hunting Match",
        _ => "Match",
    };

    public static string CityName(CityZoneCatalog.Zone? zone)
    {
        if (zone == null) return "the city";
        if (zone.Level.Contains("SimulationCenter", StringComparison.OrdinalIgnoreCase)) return "the Simulation Center";
        if (zone.Level.Contains("TrCenter", StringComparison.OrdinalIgnoreCase)) return "the Training Center";
        return zone.OwnerFaction == CityZoneCatalog.Faction.Alternatives ? "Eska" : "Nostalonia";
    }

    // Some table rows host another level (SimulationMapOverrides): the level played is the one to name.
    private string RoomMapName(int map) =>
        Name(_mapOverrides.LevelFor(map), Name(_zones.Get(map)?.LevelName, "Unknown map"));

    private string Name(string? section, string fallback) =>
        section != null && _names.Value.TryGetValue(section, out string? name) ? name : fallback;

    // Name= of each [section] in the client's English text: maps by level name, quests by Q-number.
    private static Dictionary<string, string> LoadNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in new[] { "HuxleyGame.int", "Quest.int" })
        {
            string path = ClientFolder.Resolve(Path.Combine("HuxleyGame", "Localization", "INT", file));
            if (!File.Exists(path)) continue;
            string? section = null;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']')) section = line[1..^1];
                else if (section != null && line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                {
                    names.TryAdd(section, line[5..].Trim('"'));
                    section = null;
                }
            }
        }
        return names;
    }
}
