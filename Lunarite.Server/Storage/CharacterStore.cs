using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Lunarite.Packets.Common;
using Lunarite.Resources;

namespace Lunarite.Storage;

// Character roster (name, species Index, combat Style, appearance Custom) keyed by a globally
// unique PID and owned by an account. Rows without OwnerUserId belong to account 69.
public sealed class CharacterStore
{
    private readonly ILogger _logger;

    private readonly CityZoneCatalog _cityZoneCatalog;

    private readonly AtomicJson _atomicJson;

    public CharacterStore(ILogger<CharacterStore> logger, CityZoneCatalog cityZoneCatalog,
        AtomicJson atomicJson)
    {
        _logger = logger;
        _cityZoneCatalog = cityZoneCatalog;
        _atomicJson = atomicJson;
        _chars = Load();
        _lastPid = LoadLastPid();
    }

    // HuxleyCityZoneData: Sapiens City-SP-TrCenter_1=10, Alternatives City-AT-TrCenter_1=110.
    // Retail species TypeIndex values 1/6 belong to Sapiens; 11/16 belong to Alternatives.
    public static int TrainingCenterMap(byte characterIndex) => characterIndex >= 10 ? 110 : 10;

    // HuxleyMMOPlayerController: SapiSimulationCenterMapIndex=18,
    // AlterSimulationCenterMapIndex=118 in the shipped game configuration.
    public static int SimulationCenterMap(byte characterIndex) => characterIndex >= 10 ? 118 : 18;

    // TypeIndex -> ETribeType, from HuxleyMMOSelectPlayerMenu.GetTribe.
    // Simulation Center packets carry ETribeType, not the character-selection TypeIndex.
    public static byte GetTribe(byte characterIndex) => characterIndex switch
    {
        1 => 1,  // Tribe_One
        6 => 2,  // Tribe_Syn
        11 => 3, // Tribe_Alterlabor
        16 => 4, // Tribe_Alternix
        _ => 0   // Tribe_None
    };

    public const byte CreateOk = 0;
    public const byte CreateError = 1;
    public const byte CreateMaxHave = 2;
    public const byte CreateSameName = 4;
    public const byte CreateWrongIndex = 5;
    public const byte CreateWrongName = 6;
    public const byte CreateDifferentTribe = 7;
    public const int MaxCharacters = 6; // HuxleyMMOSelectPlayerMenu.MAX_PLAYER_COUNT

    public sealed class Character
    {
        public int Pid { get; set; }

        public int OwnerUserId { get; set; } = 69;

        public string Name { get; set; } = "";

        public byte Index { get; set; }        // species TypeIndex (1=ONE-M, 6=SYN-F, 11=ATR-M, 16=ATN-F)

        public byte Style { get; set; }         // 0=None, 1=Enforcer, 2=Avenger, 3=Phantom

        public Custom Custom { get; set; } = new();

        public long DeleteAtUnixSeconds { get; set; } // 0=active; otherwise queued for deletion

        // Null for rosters saved before city location was stored.
        public int? CityMap { get; set; }

        public int CityArea { get; set; }
    }

    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "characters.json");
    private readonly string _sequencePath = Path.Combine(AppContext.BaseDirectory, "character-sequence.json");
    private readonly object _lock = new();
    // Custom uses public fields, which System.Text.Json skips unless IncludeFields is set.
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, IncludeFields = true };
    private readonly List<Character> _chars;

    private sealed class PidSequence { public int LastPid { get; set; } }

    private int _lastPid;

    private int LoadLastPid()
    {
        int rosterMax = _chars.Count == 0 ? 0 : _chars.Max(c => c.Pid);
        var sequence = _atomicJson.Read<PidSequence>(_sequencePath);
        if (sequence != null && sequence.LastPid < 0)
            throw new InvalidDataException("Character PID sequence is negative.");
        return Math.Max(rosterMax, sequence?.LastPid ?? 0);
    }

    // The high-water mark is written before the roster, outside its transaction. A failed roster
    // write only skips a PID; reusing one would attach another character's persisted data.
    private void PersistLastPid(int pid)
    {
        string generation = JsonSerializer.Serialize(new PidSequence { LastPid = pid });
        _atomicJson.Publish(_sequencePath, generation);
        _lastPid = pid;
    }

    private List<Character> Load()
    {
        try
        {
            var characters = _atomicJson.Read<List<Character>>(_path, _json);
            if (characters != null)
            {
                foreach (var character in characters) if (character.OwnerUserId == 0) character.OwnerUserId = 69;
                return characters;
            }
        }
        catch (Exception ex) { _logger.LogWarning("[Chars] load failed: {0}", ex.Message); }
        return new();
    }

    private void Save()
    {
        try { _atomicJson.Write(_path, JsonSerializer.Serialize(_chars, _json)); }
        catch (Exception ex) { _logger.LogWarning("[Chars] save failed: {0}", ex.Message); }
    }

    public List<Character> All()
    {
        lock (_lock) { return _chars.ToList(); }
    }

    public List<Character> All(int ownerUserId)
    {
        lock (_lock) { return _chars.Where(c => c.OwnerUserId == ownerUserId).ToList(); }
    }

    public Character? Get(int pid)
    {
        lock (_lock) { return _chars.FirstOrDefault(c => c.Pid == pid); }
    }

    public Character? FindByName(string name)
    {
        lock (_lock)
        {
            return _chars.FirstOrDefault(c => SameName(c.Name, name));
        }
    }

    // GM characters show to other players as "[GM] Name" (CharacterManager.DisplayNameFor), so a
    // name typed or clicked from that tag still finds the character.
    public const string GmNamePrefix = "[GM] ";

    public static string WithoutGmPrefix(string? name)
    {
        string trimmed = (name ?? "").Trim();
        return trimmed.StartsWith(GmNamePrefix.TrimEnd(), StringComparison.OrdinalIgnoreCase)
            ? trimmed[GmNamePrefix.TrimEnd().Length..].TrimStart() : trimmed;
    }

    public static bool SameName(string? stored, string? typed) =>
        string.Equals(stored, WithoutGmPrefix(typed), StringComparison.OrdinalIgnoreCase);

    public Character? GetOwned(int ownerUserId, int pid)
    {
        lock (_lock) { return _chars.FirstOrDefault(c => c.OwnerUserId == ownerUserId && c.Pid == pid); }
    }

    public (int Map, int Area) GetCityLocation(int pid)
    {
        lock (_lock)
        {
            var character = _chars.FirstOrDefault(x => x.Pid == pid);
            if (character == null) return (10, 0);
            return (character.CityMap ?? TrainingCenterMap(character.Index), character.CityArea);
        }
    }

    public bool SetCityLocation(int pid, int map, int area)
    {
        lock (_lock)
        {
            var character = _chars.FirstOrDefault(x => x.Pid == pid);
            if (character == null || !_cityZoneCatalog.IsValidArea(map, area, character.Index)) return false;
            character.CityMap = map;
            character.CityArea = area;
            Save();
            return true;
        }
    }

    // PIDs start at 1.
    public Character Create(int ownerUserId, string name, byte index, Custom custom)
    {
        lock (_lock)
        {
            int pid = checked(_lastPid + 1);
            PersistLastPid(pid);
            var character = new Character
            {
                Pid = pid, OwnerUserId = ownerUserId, Name = name, Index = index, Custom = custom
            };
            _chars.Add(character);
            Save();
            _logger.LogInformation("[Chars] created PID {0} '{1}' (species {2}).", pid, name, index);
            return character;
        }
    }

    // The client's HuxleyWordFilter.IsValidName is native and runs only on the client; its
    // character rules are not recovered, so the server refuses only what no retail client could
    // send: an empty or overlong name, a space (HuxleyMMOCustomizePlayerMenu.OnOkButtonClick
    // rejects it), invisible control/format characters, and staff words that would let a
    // player pose as a GM.
    public const int MaxNameLength = 16;

    private static readonly string[] ReservedNameWords = ["gm", "admin", "administrator", "webzen", "staff", "system"];

    public static bool IsValidName(string name)
    {
        if (name.Length is 0 or > MaxNameLength) return false;
        // Names arrive one char per byte (PacketReader), so 0x80-0xFF are halves of a Korean (CP949)
        // or other MBCS character, whatever Unicode class Latin-1 gives them; the limit counts bytes.
        if (name.Any(c => c < 0x80 && char.IsWhiteSpace(c) || c is < (char)0x80 or > (char)0xFF
                && char.GetUnicodeCategory(c) is UnicodeCategory.Control
                or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned))
            return false;
        // Whole words only, so "Dogma" or "Badminton" stay valid while "GM01" or "[GM]Nova" do not.
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        foreach (char c in name.Append(' '))
        {
            if (char.IsLetter(c)) { word.Append(char.ToLowerInvariant(c)); continue; }
            if (word.Length != 0) words.Add(word.ToString());
            word.Clear();
        }
        return !words.Any(ReservedNameWords.Contains);
    }

    // HUXLEY_RESERVED_NAMES, "Name=login,...": each character name (any case) only that account may
    // create; for everyone else it answers as a taken name.
    public static bool IsReservedForOther(string name, string? ownerLogin, string? reservedNames) =>
        (reservedNames ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Split('=', 2, StringSplitOptions.TrimEntries))
            .Any(pair => pair.Length == 2 && string.Equals(pair[0], name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pair[1], ownerLogin, StringComparison.OrdinalIgnoreCase));

    // Retail answered CreateDifferentTribe when an account already held the other tribe; this
    // server lets one account hold both (operator decision 2026-11-04).
    public byte TryCreate(int ownerUserId, string name, byte index, Custom custom, out Character? character,
        string? ownerLogin = null)
    {
        character = null;
        name = name.Trim();
        if (!IsValidName(name)) return CreateWrongName;
        if (index is not (1 or 6 or 11 or 16)) return CreateWrongIndex;
        if (IsReservedForOther(name, ownerLogin, Environment.GetEnvironmentVariable("HUXLEY_RESERVED_NAMES")))
            return CreateSameName;
        lock (_lock)
        {
            var owned = _chars.Where(x => x.OwnerUserId == ownerUserId).ToList();
            if (owned.Count >= MaxCharacters) return CreateMaxHave;
            if (_chars.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                return CreateSameName;
            int pid = checked(_lastPid + 1);
            PersistLastPid(pid);
            character = new Character { Pid = pid, OwnerUserId = ownerUserId, Name = name, Index = index, Custom = custom };
            _chars.Add(character);
            Save();
            _logger.LogInformation("[Chars] created PID {0} '{1}' (species {2}) for user {3}.", pid, name, index, ownerUserId);
            return CreateOk;
        }
    }

    // Customize Character (LunariteMenus): new face/hair data for an existing character of this account.
    public Character? TrySetCustom(int ownerUserId, string name, byte index, Custom custom)
    {
        lock (_lock)
        {
            var character = _chars.FirstOrDefault(x => x.OwnerUserId == ownerUserId && x.Index == index
                && x.DeleteAtUnixSeconds == 0 && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (character == null) return null;
            character.Custom = custom;
            Save();
            _logger.LogInformation("[Chars] PID {0} '{1}' appearance customized.", character.Pid, character.Name);
            return character;
        }
    }

    public void SetStyle(int pid, byte style)
    {
        lock (_lock)
        {
            var character = _chars.FirstOrDefault(x => x.Pid == pid);
            if (character == null) return;
            character.Style = style;
            Save();
            _logger.LogInformation("[Chars] PID {0} combat style set to {1}.", pid, style);
        }
    }

    // Combat style is chosen once. Check and write share one lock so a repeated request cannot
    // change it.
    public bool TrySetInitialStyle(int pid, byte style)
    {
        if (style is < 1 or > 3) return false;
        lock (_lock)
        {
            var character = _chars.FirstOrDefault(x => x.Pid == pid);
            if (character == null || character.Style != 0) return false;
            character.Style = style;
            Save();
            _logger.LogInformation("[Chars] PID {0} initial combat style set to {1}.", pid, style);
            return true;
        }
    }

    public byte ScheduleDelete(int pid, int minutes)
    {
        lock (_lock)
        {
            var character = _chars.FirstOrDefault(x => x.Pid == pid);
            if (character == null) return 0;
            minutes = Math.Clamp(minutes, 1, byte.MaxValue);
            character.DeleteAtUnixSeconds = DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds();
            Save();
            _logger.LogInformation("[Chars] queued PID {0} for deletion in {1} minute(s).", pid, minutes);
            return (byte)minutes;
        }
    }

    public bool Restore(int pid)
    {
        lock (_lock)
        {
            var character = _chars.FirstOrDefault(x => x.Pid == pid && x.DeleteAtUnixSeconds > 0);
            if (character == null || character.DeleteAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return false;
            character.DeleteAtUnixSeconds = 0;
            Save();
            _logger.LogInformation("[Chars] restored pending-delete PID {0}.", pid);
            return true;
        }
    }

    public static byte DeleteMinutesRemaining(Character c)
    {
        if (c.DeleteAtUnixSeconds <= 0) return 0;
        long seconds = c.DeleteAtUnixSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return seconds <= 0 ? (byte)0 : (byte)Math.Clamp((seconds + 59) / 60, 1, byte.MaxValue);
    }

    public List<int> ExpiredDeletionPids()
    {
        lock (_lock)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return _chars.Where(c => c.DeleteAtUnixSeconds > 0 && c.DeleteAtUnixSeconds <= now)
                .Select(c => c.Pid).ToList();
        }
    }

    public bool Delete(int pid)
    {
        lock (_lock)
        {
            var character = _chars.FirstOrDefault(x => x.Pid == pid);
            if (character == null) return false;
            // A characters.json without a sequence file gets its maximum PID recorded first.
            PersistLastPid(_lastPid);
            _chars.Remove(character);
            Save();
            _logger.LogInformation("[Chars] deleted PID {0}.", pid);
            return true;
        }
    }
}
