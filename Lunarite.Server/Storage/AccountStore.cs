using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Lunarite.Storage;

// Persistent Agent accounts plus the short-lived Agent -> Game authorization handoff.
public sealed class AccountStore
{
    private readonly ILogger _logger;

    public enum LoginResult : byte
    {
        Ok = 0,
        Error = 1,
        WrongId = 2,     // LOGIN_WRONGID: "only alphanumeric characters are allowed"
        SameUser = 3,
        NoneMember = 4,  // LOGIN_NONEMEMBER: "this account does not exist"
        WrongPassword = 5,
        Disabled = 6     // LOGIN_DISABLED: banned by a GM
    }

    public enum RegistrationResult : byte
    {
        Ok = 0,
        Error = 1,
        WrongId = 2,
        Duplicate = 3
    }

    public sealed class Account
    {
        public int UserId { get; set; }

        public string Login { get; set; } = "";

        public string Salt { get; set; } = "";

        public string PasswordHash { get; set; } = "";

        public int Cash { get; set; }

        public bool Banned { get; set; }
    }

    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "accounts.json");
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly List<Account> _accounts;

    private readonly AtomicJson _atomicJson;

    public AccountStore(ILogger<AccountStore> logger, AtomicJson atomicJson)
    {
        _logger = logger;
        _atomicJson = atomicJson;
        _accounts = Load();
        // A listed GM login with no account cannot be registered (Register refuses it); say so.
        foreach (string gmLogin in MissingGmLogins(Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS")))
            _logger.LogWarning("[Accounts] GM login '{0}' in HUXLEY_GM_LOGINS has no account; create it with "
                + "HUXLEY_RESET_ACCOUNT and HUXLEY_RESET_PASSWORD.", gmLogin);
    }

    public IReadOnlyList<string> MissingGmLogins(string? gmLogins)
    {
        lock (_lock)
            return (gmLogins ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(gmLogin => !_accounts.Any(x => string.Equals(x.Login, gmLogin, StringComparison.OrdinalIgnoreCase)))
                .ToList();
    }

    private List<Account> Load()
    {
        try
        {
            return _atomicJson.Read<List<Account>>(_path, _json) ?? [];
        }
        catch (Exception ex) { _logger.LogWarning("[Accounts] load failed: {0}", ex.Message); }
        return [];
    }

    private void Save()
    {
        try { _atomicJson.Write(_path, JsonSerializer.Serialize(_accounts, _json)); }
        catch (Exception ex) { _logger.LogWarning("[Accounts] save failed: {0}", ex.Message); }
    }

    public bool TryRegister(string login, string password, out int userId)
        => Register(login, password, out userId) == RegistrationResult.Ok;

    // LoginMsg[2] / NewMemberMsg[2]: account names are alphanumeric only.
    public static bool IsValidLogin(string login) =>
        login.Length > 0 && login.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9'));

    public RegistrationResult Register(string login, string password, out int userId)
        => Register(login, password, out userId, out _);

    // hashed reports whether PBKDF2 ran; the per-address registration limit counts those.
    public RegistrationResult Register(string login, string password, out int userId, out bool hashed)
    {
        userId = 0;
        hashed = false;
        login = login.Trim();
        if (!IsValidLogin(login)) return RegistrationResult.WrongId;
        if (password.Length == 0) return RegistrationResult.Error;
        // GM status follows the login name, so a listed GM login that has no account yet must not
        // be claimable through op6 (the operator creates it with HUXLEY_RESET_ACCOUNT).
        if (World.GmCommands.IsGmLogin(login, Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS")))
            return RegistrationResult.Duplicate;
        // A taken login answers before hashing, so repeating op6 with it costs no PBKDF2.
        lock (_lock)
            if (_accounts.Any(x => string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase)))
                return RegistrationResult.Duplicate;
        // PBKDF2 runs outside the lock so guesses cannot stall every account and cash lookup.
        hashed = true;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        string hash = Hash(password, salt);
        lock (_lock)
        {
            if (_accounts.Any(x => string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase)))
                return RegistrationResult.Duplicate;
            userId = _accounts.Count == 0 ? 69 : Math.Max(69, _accounts.Max(x => x.UserId) + 1);
            _accounts.Add(new Account
            {
                UserId = userId,
                Login = login,
                Salt = Convert.ToBase64String(salt),
                PasswordHash = hash
            });
            Save();
            return RegistrationResult.Ok;
        }
    }

    // A clean private-server install provisions the first submitted credentials. Later logins
    // authenticate normally; explicit op6 registration creates additional accounts.
    public bool TryLogin(string login, string password, out int userId)
        => Authenticate(login, password, out userId) == LoginResult.Ok;

    // HuxleyPCMMOUIInteraction.OnLogin exposes distinct retail messages for an unknown ID (2)
    // and a wrong password (5). Preserve that distinction at the authentication boundary while
    // retaining the private server's one-time first-account provisioning behavior.
    public LoginResult Authenticate(string login, string password, out int userId)
    {
        userId = 0;
        login = login.Trim();
        if (login.Length == 0) return LoginResult.WrongId;
        if (password.Length == 0) return LoginResult.WrongPassword;
        string storedSaltText, storedHashText;
        int accountUserId;
        bool banned;
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(x => string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                // HUXLEY_PROVISION_FIRST_ACCOUNT=0 turns the fresh-install convenience off so a
                // wiped accounts.json cannot be claimed by whoever logs in first.
                if (_accounts.Count != 0
                    || Environment.GetEnvironmentVariable("HUXLEY_PROVISION_FIRST_ACCOUNT") == "0")
                    return IsValidLogin(login) ? LoginResult.NoneMember : LoginResult.WrongId;
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                account = new Account
                {
                    UserId = 69, Login = login, Salt = Convert.ToBase64String(salt), PasswordHash = Hash(password, salt)
                };
                _accounts.Add(account);
                Save();
                _logger.LogInformation("[Accounts] provisioned first account '{0}' as user {1}.", login, account.UserId);
            }
            storedSaltText = account.Salt;
            storedHashText = account.PasswordHash;
            accountUserId = account.UserId;
            banned = account.Banned;
        }
        // PBKDF2 runs outside the lock so guesses cannot stall every account and cash lookup.
        byte[] storedSalt;
        byte[] expected;
        try
        {
            storedSalt = Convert.FromBase64String(storedSaltText);
            expected = Convert.FromBase64String(storedHashText);
        }
        catch { return LoginResult.Error; }
        byte[] actual = Convert.FromBase64String(Hash(password, storedSalt));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) return LoginResult.WrongPassword;
        if (banned) return LoginResult.Disabled;
        userId = accountUserId;
        return LoginResult.Ok;
    }

    // Local operator recovery. The caller supplies credentials through process environment so
    // neither the password nor its command-line quoting is persisted in logs/process arguments.
    // It also creates a login listed in HUXLEY_GM_LOGINS, which op6 refuses to register.
    public bool TryResetPassword(string login, string password, out int userId)
    {
        userId = 0;
        login = login.Trim();
        if (login.Length == 0 || password.Length == 0) return false;
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(x =>
                string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                if (!IsValidLogin(login)
                    || !World.GmCommands.IsGmLogin(login, Environment.GetEnvironmentVariable("HUXLEY_GM_LOGINS")))
                    return false;
                account = new Account
                {
                    UserId = _accounts.Count == 0 ? 69 : Math.Max(69, _accounts.Max(x => x.UserId) + 1),
                    Login = login
                };
                _accounts.Add(account);
                _logger.LogInformation("[Accounts] created listed GM login '{0}' as user {1}.", login, account.UserId);
            }
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            account.Salt = Convert.ToBase64String(salt);
            account.PasswordHash = Hash(password, salt);
            userId = account.UserId;
            Save();
            return true;
        }
    }

    // Cash is account-scoped: the client requests it before/without selecting a character.
    public string? LoginFor(int userId)
    {
        lock (_lock) return _accounts.FirstOrDefault(x => x.UserId == userId)?.Login;
    }

    // GM ban/unban by login; returns the account's user id, or 0 when no such account exists.
    public int SetBanned(string login, bool banned)
    {
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(x => string.Equals(x.Login, login.Trim(), StringComparison.OrdinalIgnoreCase));
            if (account == null) return 0;
            if (account.Banned != banned) { account.Banned = banned; Save(); }
            return account.UserId;
        }
    }

    public int GetCash(int userId)
    {
        lock (_lock) return _accounts.FirstOrDefault(x => x.UserId == userId)?.Cash ?? 0;
    }

    public bool TryAdjustCash(int userId, int delta, out int balance)
    {
        balance = 0;
        lock (_lock)
        {
            var account = _accounts.FirstOrDefault(x => x.UserId == userId);
            if (account == null) return false;
            long next = (long)account.Cash + delta;
            if (next < 0 || next > int.MaxValue) return false;
            account.Cash = balance = (int)next;
            Save();
            return true;
        }
    }

    private static string Hash(string password, byte[] salt) => Convert.ToBase64String(
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32));
}

public sealed class AccountSessionStore
{
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(60);

    // Game Connects per address that claim nothing: a burst, then one per interval. See TryClaim.
    public const int ConnectBurst = 5;
    public static readonly TimeSpan ConnectRefill = TimeSpan.FromSeconds(2);

    // Unthrottled PBKDF2 logins are an online password guesser and op6 an account factory.
    // Wrong logins are limited per account name, and more loosely per source address so a
    // shared NAT (cafe, dorm, mobile carrier) is not locked out by one neighbour.
    public const int MaxLoginFailuresPerLogin = 10;
    public const int MaxLoginFailuresPerAddress = 50;
    public static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(5);
    public const int MaxRegistrations = 20;
    public static readonly TimeSpan RegistrationWindow = TimeSpan.FromHours(1);

    private sealed record Ticket(int UserId, DateTimeOffset Expires);

    private sealed class ConnectBucket
    {
        public double Tokens;
        public DateTimeOffset Updated;
    }

    // A login reserved against both limits; EndLogin keeps it as a failure or gives it back.
    public sealed record LoginAttempt(string AddressKey, string LoginKey, DateTimeOffset Time);

    private readonly object _lock = new();
    // Outstanding Game handoffs by address key, at most one per account (Issue replaces it).
    private readonly Dictionary<string, List<Ticket>> _tickets = [];
    private readonly Dictionary<string, ConnectBucket> _connectBuckets = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _loginFailuresByAddress = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _loginFailuresByLogin = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _registrations = [];

    // The handoff clock; tests replace it to step time.
    internal Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public void Issue(IPAddress address, int userId, IPAddress? clientLocalAddress = null,
        bool trustClientLocalAddress = false)
    {
        string key = Key(address, clientLocalAddress, trustClientLocalAddress);
        lock (_lock)
        {
            var now = Clock();
            PurgeLocked(now);
            // One outstanding handoff per account: a repeated SelectServer replaces the last one,
            // so one Agent login cannot bind several Game links.
            foreach (var issued in _tickets.ToList())
            {
                issued.Value.RemoveAll(ticket => ticket.UserId == userId);
                if (issued.Value.Count == 0) _tickets.Remove(issued.Key);
            }
            if (!_tickets.TryGetValue(key, out var tickets)) _tickets[key] = tickets = [];
            tickets.Add(new Ticket(userId, now + TicketLifetime));
        }
    }

    // Retail SendConnect carries no account token, so a Game Connect is matched to its Agent
    // login by address alone. It claims only when exactly one account holds a ticket for that
    // address key; two accounts there (or, in trusted local-IP mode, two clients reporting the
    // same public/local pair) are ambiguous, so the Connect is refused rather than assigned by
    // FIFO. As in the original handoff, those tickets are dropped: kept, each owner's retry
    // would still find the other's stale ticket and be refused until it expired. ConnectHandler
    // closes the link after any refusal, so each Game socket gets one try. The Verify UID never
    // picks the account; VerifyHandler only checks it equals the one bound here.
    //
    // Residual: a client on the same public address (CGNAT, shared Wi-Fi) that blind-polls
    // Connect can still take a neighbour's handoff if one poll lands between that neighbour's
    // SelectServer answer and its own Connect, usually a round trip. Connects that claim nothing
    // spend a per-address token (burst ConnectBurst, one back per ConnectRefill), so such polling
    // gets about one try per two seconds; a poller that keeps the bucket empty, or keeps its own
    // ticket outstanding, refuses Game logins on that address only while it keeps doing so.
    // Successful claims cost nothing, so families and LAN parties behind one IP are not limited.
    public bool TryClaim(IPAddress address, out int userId,
        IPAddress? clientLocalAddress = null, bool trustClientLocalAddress = false)
    {
        string key = Key(address, clientLocalAddress, trustClientLocalAddress);
        lock (_lock)
        {
            var now = Clock();
            PurgeLocked(now);
            userId = 0;
            if (!_connectBuckets.TryGetValue(key, out var bucket))
                _connectBuckets[key] = bucket = new ConnectBucket { Tokens = ConnectBurst, Updated = now };
            if (bucket.Tokens < 1) return false;
            if (_tickets.Remove(key, out var tickets) && tickets.Count == 1)
            {
                userId = tickets[0].UserId;
                return true;
            }
            bucket.Tokens--;
        }
        return false;
    }

    // Reserves one wrong-login slot for this address and account name before the password is
    // hashed, so parallel Agent sockets cannot run more guesses than the limits allow. False:
    // answer LOGIN_ERROR without hashing.
    public bool TryBeginLogin(IPAddress address, string login, out LoginAttempt? attempt)
    {
        var now = DateTimeOffset.UtcNow;
        var candidate = new LoginAttempt(Normalize(address), login.Trim().ToLowerInvariant(), now);
        lock (_lock)
        {
            attempt = null;
            if (CountRecentLocked(_loginFailuresByAddress, candidate.AddressKey, LoginFailureWindow, now)
                    >= MaxLoginFailuresPerAddress
                || CountRecentLocked(_loginFailuresByLogin, candidate.LoginKey, LoginFailureWindow, now)
                    >= MaxLoginFailuresPerLogin)
                return false;
            AddLocked(_loginFailuresByAddress, candidate.AddressKey, LoginFailureWindow, now);
            AddLocked(_loginFailuresByLogin, candidate.LoginKey, LoginFailureWindow, now);
            attempt = candidate;
            return true;
        }
    }

    // A wrong password or unknown login keeps its reserved slot; anything else gives it back.
    public void EndLogin(LoginAttempt attempt, bool failed)
    {
        if (failed) return;
        lock (_lock)
        {
            RemoveLocked(_loginFailuresByAddress, attempt.AddressKey, attempt.Time);
            RemoveLocked(_loginFailuresByLogin, attempt.LoginKey, attempt.Time);
        }
    }

    // Reserves one op6 registration for this address; give it back with CancelRegistration when
    // no password was hashed.
    public bool TryBeginRegistration(IPAddress address, out DateTimeOffset reservation)
    {
        reservation = DateTimeOffset.UtcNow;
        string key = Normalize(address);
        lock (_lock)
        {
            if (CountRecentLocked(_registrations, key, RegistrationWindow, reservation) >= MaxRegistrations)
                return false;
            AddLocked(_registrations, key, RegistrationWindow, reservation);
            return true;
        }
    }

    public void CancelRegistration(IPAddress address, DateTimeOffset reservation)
    {
        lock (_lock) RemoveLocked(_registrations, Normalize(address), reservation);
    }

    private static int CountRecentLocked(Dictionary<string, List<DateTimeOffset>> attempts, string key,
        TimeSpan window, DateTimeOffset now)
    {
        if (!attempts.TryGetValue(key, out var times)) return 0;
        times.RemoveAll(time => time <= now - window);
        return times.Count;
    }

    private static void AddLocked(Dictionary<string, List<DateTimeOffset>> attempts, string key,
        TimeSpan window, DateTimeOffset now)
    {
        foreach (string staleKey in attempts.Keys.ToList())
        {
            attempts[staleKey].RemoveAll(time => time <= now - window);
            if (attempts[staleKey].Count == 0) attempts.Remove(staleKey);
        }
        if (!attempts.TryGetValue(key, out var times)) attempts[key] = times = [];
        times.Add(now);
    }

    private static void RemoveLocked(Dictionary<string, List<DateTimeOffset>> attempts, string key,
        DateTimeOffset time)
    {
        if (!attempts.TryGetValue(key, out var times)) return;
        times.Remove(time);
        if (times.Count == 0) attempts.Remove(key);
    }

    // The client sends its local IPv4 in both Agent and Game Connect. It is not authenticated:
    // use it only for an explicitly trusted private deployment, never as the default identity
    // boundary. Invalid/missing local addresses retain the public-IP collision guard.
    private static string Key(IPAddress publicAddress, IPAddress? clientLocalAddress, bool trustClientLocalAddress)
    {
        string key = Normalize(publicAddress);
        if (trustClientLocalAddress && clientLocalAddress != null)
        {
            var local = clientLocalAddress.IsIPv4MappedToIPv6
                ? clientLocalAddress.MapToIPv4() : clientLocalAddress;
            if (local.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && local.GetAddressBytes()[0] is > 0 and < 224)
                key += "|" + Normalize(local);
        }
        return key;
    }

    private static string Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();

    private void PurgeLocked(DateTimeOffset now)
    {
        foreach (var issued in _tickets.ToList())
        {
            issued.Value.RemoveAll(ticket => ticket.Expires <= now);
            if (issued.Value.Count == 0) _tickets.Remove(issued.Key);
        }
        // Refill every bucket; a full one is the same as none, so drop it.
        foreach (var refill in _connectBuckets.ToList())
        {
            var bucket = refill.Value;
            if (now > bucket.Updated)
            {
                bucket.Tokens = Math.Min(ConnectBurst,
                    bucket.Tokens + (now - bucket.Updated) / ConnectRefill);
                bucket.Updated = now;
            }
            if (bucket.Tokens >= ConnectBurst) _connectBuckets.Remove(refill.Key);
        }
    }
}
