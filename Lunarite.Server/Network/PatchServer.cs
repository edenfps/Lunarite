using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Resources;

namespace Lunarite.Network;

// Update server for WZLauncher, the client launcher.
// It only uses the stock launcher's code paths:
//   1. Version_2.txt names one patch-set version L (see CurrentVersion; yyyyMMddHHmmss sorts after
//      the retail 00000012) and a hop to it from 00000012. Any other installed version,
//      or none, reads as unknown, which the launcher also answers with an update.
//   2. The hop has no patch file, so the update fails into WZUpdater::Recover, the full-file check:
//      FullFile.txt lists every manifest file with its size and CRC-32, and the launcher downloads
//      each one that differs from the installation.
//   3. Recover succeeds, the launcher writes L into Version_2.txt and starts the game.
// With a base folder (HUXLEY_PATCH_BASE, a clean US client) the patch set is the whole client: every
// base file, with the payload's manifest files in place of the base copies. The launcher then
// installs the game into an empty folder, since every file differs from a missing one.
// A Korean launcher (Accept-Language: ko) gets the base copies of patch/korean.txt instead: Webzen's
// Korean text and UI skin, where English launchers keep the payload's English-in-KOR text.
// GM consoles: an address a GM account logged in from (ReportGm, kept 30 days in gm-addresses.txt) is
// served the files in HUXLEY_PATCH_GM_ROOT (the operator's dev overlay: console keys, unrestricted
// scripts) in place of the payload's, under its own version (GmVersion), so any launcher, including
// the public one, installs them and a later player-version check puts the player files back.
// Configs are served with m_strMMOIP set to the address the launcher reached this server on, so one
// patch set works over loopback, LAN or a VPN such as Radmin. Behind NAT that address is the LAN
// one, so HUXLEY_GAME_PUBLIC_IP overrides it, as it does the game address the Agent advertises.
public sealed class PatchServer : IDisposable
{
    public const string PatchDir = "/huxley/Patch/Korea/Service/City/";
    public const string FullDir = "/huxley/Full/Korea/Service/City/";
    public const string BaseVersion = "00000012";
    public const string LauncherVersion = "0003.0000";

    private readonly ILogger _logger;
    private readonly string _root;
    private readonly string? _base;
    // The file lists below are rebuilt by Reload (patch-reload trigger); file contents are re-checked per request anyway.
    private IReadOnlyList<string> _files = [];
    // Request path (backslashes, any case) -> file as listed; manifest files come from _root.
    private Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _fromPayload = new(StringComparer.OrdinalIgnoreCase);
    // Base files a Korean launcher gets instead of the payload's (patch/korean.txt).
    private HashSet<string> _korean = new(StringComparer.OrdinalIgnoreCase);
    // Manifest files with a GM variant in _gmRoot.
    private HashSet<string> _gm = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _gmRoot;
    // Korean files with a merged copy in _koreanRoot (Webzen's Korean text with
    // the keys it lacks filled from our English), served to Korean launchers instead of the base copy.
    private HashSet<string> _koreanOverride = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _koreanRoot;
    private readonly string _versionPath;
    private readonly ConcurrentDictionary<string, (long Length, DateTime Written, uint Crc)> _crcs = new();
    private readonly object _versionLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    // The stock launcher sends its whole HTTP/1.0 request at once. A request whose headers take
    // longer than IoTimeoutMilliseconds in total is cut off. At most MaxConnections requests are
    // served at once and at most MaxConnectionsPerAddress from one address; past either, the
    // request gets 503 at once, so one host cannot hold every slot and launchers fail fast
    // instead of hanging in the backlog. A download fails when one 16 KB chunk stalls for
    // SendTimeoutMilliseconds or the body runs slower than MinBytesPerSecond overall.
    // Launcher 1.1 downloads six files at once, so one address gets room for two such launchers
    // (players sharing a home connection), and the total for about ten.
    private const int MaxConnections = 64;
    internal const int MaxConnectionsPerAddress = 12;
    private const int ChunkSize = 1 << 14;
    private const int MinBytesPerSecond = 8 << 10;
    private int _active;
    private readonly ConcurrentDictionary<IPAddress, int> _activeByAddress = new();
    internal int IoTimeoutMilliseconds { get; set; } = 10_000;
    internal int SendTimeoutMilliseconds { get; set; } = 60_000;
    // A folder holding only the files players should get; never the operator's play client.
    public const string DefaultPayloadRoot = @"C:\Games\Huxley_PatchPayload";

    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    public PatchServer(string root, IReadOnlyList<string> files, string versionPath, ILogger<PatchServer> logger,
        string? baseRoot = null, IReadOnlyList<string>? korean = null, string? gmRoot = null,
        string? koreanRoot = null)
    {
        _logger = logger;
        _root = root;
        _gmRoot = gmRoot;
        _koreanRoot = koreanRoot;
        _base = baseRoot;
        _versionPath = versionPath;
        BuildSets(files, korean);
        LoadCrcCache();
    }

    // Manifest and korean.txt the sets were read from (StartFromEnvironment); Reload reads them again.
    private string? _manifestPath, _koreanListPath;
    private Timer? _reloadTimer;

    // Every list the server answers from: manifest files, base files under them, Korean and GM variants.
    private void BuildSets(IReadOnlyList<string> files, IReadOnlyList<string>? korean)
    {
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fromPayload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var koreanFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gmFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var koreanOverride = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<string>(files);
        foreach (string file in files) { byPath[file] = file; fromPayload.Add(file); }
        if (_base != null)
            foreach (string file in BaseFiles(_base))
            {
                if (byPath.TryAdd(file, file)) all.Add(file);
                if (korean != null && korean.Any(entry => entry.EndsWith('\\')
                        ? file.StartsWith(entry, StringComparison.OrdinalIgnoreCase)
                        : file.Equals(entry, StringComparison.OrdinalIgnoreCase)))
                    koreanFiles.Add(byPath[file]);
            }
        if (_gmRoot != null)
            foreach (string file in files)
                if (File.Exists(ClientFolder.Resolve(_gmRoot, file))) gmFiles.Add(file);
        if (_koreanRoot != null)
            foreach (string file in koreanFiles)
                if (File.Exists(ClientFolder.Resolve(_koreanRoot, file))) koreanOverride.Add(file);
        // Swapped whole, so a request in flight sees either the old lists or the new ones.
        (_byPath, _fromPayload, _korean, _gm, _koreanOverride, _files) = (byPath, fromPayload, koreanFiles, gmFiles, koreanOverride, all);
    }

    // Re-reads the manifest, korean.txt and the GM/Korean variant folders without a restart, for a payload push that
    // adds or removes files (deploy/deploy.sh --client-only touches the trigger file). Returns false with no manifest.
    public bool Reload()
    {
        if (_manifestPath == null || !File.Exists(_manifestPath)) return false;
        try
        {
            BuildSets(ReadManifest(_manifestPath),
                _koreanListPath != null && File.Exists(_koreanListPath) ? ReadManifest(_koreanListPath) : null);
            _logger.LogInformation("[Patch] reloaded: {0} files, {1} GM variants, version {2}, GM version {3}.",
                _files.Count, _gm.Count, CurrentVersion(), GmVersion());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Patch] reload failed: {0}", ex.Message);
            return false;
        }
    }

    internal void UseManifest(string manifest, string? koreanList = null)
    {
        _manifestPath = manifest;
        _koreanListPath = koreanList;
    }

    // Polls for the trigger file every few seconds; the file is deleted, then the lists are rebuilt.
    private void WatchReloadTrigger(string triggerPath)
    {
        _reloadTimer = new Timer(_ =>
        {
            try
            {
                if (!File.Exists(triggerPath)) return;
                File.Delete(triggerPath);
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            Reload();
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private static readonly ConcurrentDictionary<IPAddress, DateTime> GmAddresses = new();
    private static readonly TimeSpan GmAddressLifetime = TimeSpan.FromDays(30);
    private static string GmAddressesPath => Path.Combine(AppContext.BaseDirectory, "gm-addresses.txt");

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public static bool IsGm(IPAddress address) =>
        GmAddresses.TryGetValue(Normalize(address), out DateTime expires) && expires > DateTime.UtcNow;

    // Called when a GM account logs in; the address's next launcher check installs the GM files.
    public static void ReportGm(IPAddress address)
    {
        if (address.Equals(IPAddress.None)) return;
        GmAddresses[Normalize(address)] = DateTime.UtcNow + GmAddressLifetime;
        try
        {
            File.WriteAllLines(GmAddressesPath, GmAddresses.Where(x => x.Value > DateTime.UtcNow)
                .Select(x => $"{x.Key} {x.Value.Ticks.ToString(CultureInfo.InvariantCulture)}"));
        }
        catch (IOException) { }
    }

    internal static void LoadGmAddresses()
    {
        if (!File.Exists(GmAddressesPath)) return;
        foreach (string line in File.ReadLines(GmAddressesPath))
        {
            string[] parts = line.Split(' ');
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out IPAddress? address)
                && long.TryParse(parts[1], out long ticks) && ticks > DateTime.UtcNow.Ticks)
                GmAddresses[address] = new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    // Korean launchers say so in Accept-Language. The game server reads the language reported from a
    // player's address (ReportedLanguage) for its own messages.
    private static readonly ConcurrentDictionary<IPAddress, string> LanguageByAddress = new();

    public static string? ReportedLanguage(IPAddress address) =>
        LanguageByAddress.TryGetValue(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address, out string? language)
            ? language : null;

    internal static void ReportLanguage(IPAddress address, bool korean) =>
        LanguageByAddress[address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address] = korean ? "ko" : "en";

    internal static bool IsKorean(string? acceptLanguage) =>
        acceptLanguage?.TrimStart().StartsWith("ko", StringComparison.OrdinalIgnoreCase) == true;

    // A Korean-only file is in patch/korean.txt but not the payload: English launchers never get it.
    private bool Serves(string file, bool korean) => korean || !_korean.Contains(file) || _fromPayload.Contains(file);

    // CRCs survive restarts in <version file>.crc, so a full client (about 22 GB) is read once, not
    // on every start. Lines: full path, length, last write (UTC ticks), CRC; tab-separated.
    private string CrcCachePath => _versionPath + ".crc";
    private int _crcsDirty;

    private void LoadCrcCache()
    {
        if (!File.Exists(CrcCachePath)) return;
        try
        {
            foreach (string line in File.ReadLines(CrcCachePath))
            {
                string[] parts = line.Split('\t');
                if (parts.Length == 4 && long.TryParse(parts[1], out long length) && long.TryParse(parts[2], out long ticks)
                    && uint.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint crc))
                    _crcs[parts[0]] = (length, new DateTime(ticks, DateTimeKind.Utc), crc);
            }
        }
        catch (IOException ex) { _logger.LogWarning("[Patch] CRC cache unreadable: {0}", ex.Message); }
    }

    private void SaveCrcCache()
    {
        if (Interlocked.Exchange(ref _crcsDirty, 0) == 0) return;
        try
        {
            File.WriteAllLines(CrcCachePath + ".tmp", _crcs.Select(entry => string.Join('\t', entry.Key,
                entry.Value.Length.ToString(CultureInfo.InvariantCulture),
                entry.Value.Written.Ticks.ToString(CultureInfo.InvariantCulture),
                entry.Value.Crc.ToString("X8", CultureInfo.InvariantCulture))));
            File.Move(CrcCachePath + ".tmp", CrcCachePath, true);
        }
        catch (IOException ex) { _logger.LogWarning("[Patch] CRC cache not saved: {0}", ex.Message); }
    }

    // Every file of the base client except what a player's install should not get: logs,
    // screenshots, shortcuts, the retail installer's file lists, and paths the stock launcher
    // cannot request (it sends them unescaped, so spaces and non-ASCII names do not survive).
    public static IEnumerable<string> BaseFiles(string baseRoot)
    {
        string full = Path.GetFullPath(baseRoot);
        foreach (string path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.GetRelativePath(full, path).Replace('/', '\\');
            string[] parts = file.Split('\\');
            string name = parts[^1];
            if (!file.All(c => c > ' ' && c < 127)) continue;
            if (parts.Length == 1 && (name.Equals("list.txt", StringComparison.OrdinalIgnoreCase)
                || name.Equals("files.txt", StringComparison.OrdinalIgnoreCase))) continue;
            if (parts.Any(part => part.Equals("Logs", StringComparison.OrdinalIgnoreCase)
                || part.Equals("ScreenShots", StringComparison.OrdinalIgnoreCase))) continue;
            if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsGeneratedConfig(file)) continue;
            yield return file;
        }
    }

    // HuxleyMMOGame\Config\HuxleyMMO*.ini: the game builds these from the default layers and keeps
    // the player's settings in them. Listing them would make every full-file check put the stock
    // copy back, so the game rebuilt it and the player lost their settings on every update.
    public static bool IsGeneratedConfig(string file) =>
        file.StartsWith(@"HuxleyMMOGame\Config\HuxleyMMO", StringComparison.OrdinalIgnoreCase)
        && file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
        && file.IndexOf('\\', @"HuxleyMMOGame\Config\".Length) < 0;

    private string Locate(string file, bool korean = false, bool gm = false) =>
        gm && _gmRoot != null && _gm.Contains(file) ? ClientFolder.Resolve(_gmRoot, file)
        : korean && _koreanRoot != null && _koreanOverride.Contains(file) ? ClientFolder.Resolve(_koreanRoot, file)
        : ClientFolder.Resolve(_base != null && (!_fromPayload.Contains(file) || korean && _korean.Contains(file))
            ? _base : _root, file);

    // HUXLEY_PATCH_PORT (default 8090, 0 = off), HUXLEY_PATCH_ROOT (default DefaultPayloadRoot),
    // HUXLEY_PATCH_MANIFEST (default patch/manifest.txt found above the server directory),
    // HUXLEY_PATCH_BASE (optional clean US client served under the payload; see the class comment).
    public static PatchServer? StartFromEnvironment(ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<PatchServer>();
        int port = int.TryParse(Environment.GetEnvironmentVariable("HUXLEY_PATCH_PORT"), out int configuredPort)
            ? configuredPort : 8090;
        if (port <= 0) return null;
        string root = Environment.GetEnvironmentVariable("HUXLEY_PATCH_ROOT") ?? DefaultPayloadRoot;
        string? manifest = Environment.GetEnvironmentVariable("HUXLEY_PATCH_MANIFEST") ?? FindManifest();
        if (!Directory.Exists(root) || manifest == null || !File.Exists(manifest))
        {
            logger.LogWarning("[Patch] update server off: payload {0} or patch/manifest.txt not found.", root);
            return null;
        }
        string? baseRoot = Environment.GetEnvironmentVariable("HUXLEY_PATCH_BASE") is { Length: > 0 } configured
            ? configured : null;
        if (baseRoot != null && !Directory.Exists(baseRoot))
        {
            logger.LogWarning("[Patch] HUXLEY_PATCH_BASE {0} not found; serving the payload only.", baseRoot);
            baseRoot = null;
        }
        string korean = Path.Combine(Path.GetDirectoryName(manifest)!, "korean.txt");
        string? gmRoot = Environment.GetEnvironmentVariable("HUXLEY_PATCH_GM_ROOT") is { Length: > 0 } gmConfigured
            && Directory.Exists(gmConfigured) ? gmConfigured : null;
        string? koreanRoot = Environment.GetEnvironmentVariable("HUXLEY_PATCH_KOREAN_ROOT") is { Length: > 0 } koConfigured
            && Directory.Exists(koConfigured) ? koConfigured : null;
        LoadGmAddresses();
        var server = new PatchServer(root, ReadManifest(manifest),
            Path.Combine(AppContext.BaseDirectory, "patch-version.txt"), logger, baseRoot,
            File.Exists(korean) ? ReadManifest(korean) : null, gmRoot, koreanRoot);
        server._manifestPath = manifest;
        server._koreanListPath = korean;
        // HUXLEY_STATUS_FILE's folder also takes patch-reload (deploy/deploy.sh --client-only).
        if (Environment.GetEnvironmentVariable("HUXLEY_STATUS_FILE") is { Length: > 0 } statusFile)
            server.WatchReloadTrigger(Path.Combine(Path.GetDirectoryName(statusFile) ?? "", "patch-reload"));
        if (gmRoot != null) logger.LogInformation("[Patch] GM variants: {0} files from {1}.", server._gm.Count, gmRoot);
        if (koreanRoot != null)
            logger.LogInformation("[Patch] Korean overrides: {0} files from {1}.", server._koreanOverride.Count, koreanRoot);
        return server.Start(port) ? server : null;
    }

    public static IReadOnlyList<string> ReadManifest(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Replace('/', '\\').TrimStart('\\'))
            .Where(line => !line.Split('\\').Contains(".."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? FindManifest()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "patch", "manifest.txt");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    public bool Start(int port)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _stop = new CancellationTokenSource();
            _ = AcceptLoop(_stop.Token);
            // Hash every file now; the first launcher request would otherwise wait for it.
            _ = Task.Run(() => _logger.LogInformation("[Patch] launcher update server on port {0}: {1} files from {2}{3}, version {4}.",
                Port, _files.Count, _root, _base != null ? $" over {_base}" : "", CurrentVersion()));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Patch] update server unavailable on port {0}: {1}", port, ex.Message);
            return false;
        }
    }

    // The version is the UTC time (yyyyMMddHHmmss) the patch set's content was first seen, kept in
    // _versionPath with a fingerprint of every file's path, size and CRC. Rewriting a file with the
    // same bytes (the dev client launcher re-saves its INIs) keeps the version.
    public string CurrentVersion()
    {
        var fingerprint = new StringBuilder();
        foreach (string file in _files)
            if (TryGetCrc(file, out long length, out uint crc))
                fingerprint.Append(file).Append('|').Append(length).Append('|').Append(crc).Append('\n');
        // One version for both languages: a change to the Korean files updates everyone's version.
        foreach (string file in _korean)
            if (TryGetCrc(file, out long length, out uint crc, korean: true))
                fingerprint.Append("ko|").Append(file).Append('|').Append(length).Append('|').Append(crc).Append('\n');
        foreach (string file in _gm)
            if (TryGetCrc(file, out long length, out uint crc, gm: true))
                fingerprint.Append("gm|").Append(file).Append('|').Append(length).Append('|').Append(crc).Append('\n');
        // Served INIs are rewritten (RenderConfig); a new rewrite rule must reach launchers as a new version.
        fingerprint.Append("render|").Append(string.Join(',', AddressKeys)).Append('\n');
        SaveCrcCache();
        string hash = Crc32(Encoding.UTF8.GetBytes(fingerprint.ToString())).ToString("X8", CultureInfo.InvariantCulture);
        lock (_versionLock)
        {
            string[] saved = File.Exists(_versionPath) ? File.ReadAllText(_versionPath).Split(' ') : [];
            if (saved.Length == 2 && saved[1] == hash) return saved[0];
            string version = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            // A new version must sort after the old one, even when both fall in the same second.
            if (saved.Length == 2 && string.CompareOrdinal(version, saved[0]) <= 0
                && DateTime.TryParseExact(saved[0], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime previous))
                version = previous.AddSeconds(1).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.WriteAllText(_versionPath, $"{version} {hash}");
            return version;
        }
    }

    // GM addresses get their own version one second after the player one: a launcher that last
    // installed either set sees the other as new and runs the full-file check.
    public string GmVersion() =>
        DateTime.TryParseExact(CurrentVersion(), "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime version)
            ? version.AddSeconds(1).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) : CurrentVersion();

    public string VersionFile(bool gm = false)
    {
        string latest = gm && _gm.Count > 0 ? GmVersion() : CurrentVersion();
        return $"{BaseVersion} {latest} Patch/{BaseVersion}_{latest}.rtp 0\r\n{latest} {latest} none 0\r\n";
    }

    public string FullFileList(string mmoIp, bool korean = false, bool gm = false)
    {
        var text = new StringBuilder();
        foreach (string file in _files)
        {
            if (!Serves(file, korean)) continue;
            byte[]? rendered = RenderConfig(file, mmoIp, korean, gm);
            long length;
            uint crc;
            if (rendered != null) (length, crc) = (rendered.Length, Crc32(rendered));
            else if (!TryGetCrc(file, out length, out crc, korean, gm)) continue;
            text.Append('"').Append(file).Append("\" ").Append(length.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(crc.ToString("X", CultureInfo.InvariantCulture)).Append("\r\n");
        }
        return text.ToString();
    }

    // Config files that name the MMO server get the caller's address, and so does the in-game shop's
    // catalog host (m_strShopServerIP, fetched over HTTP port 80 whenever a vendor or the shop
    // opens; CashShopWebServer answers it in this process). With 127.0.0.1 there, a player's client
    // waited on its own empty port 80 at every vendor. Other bytes are untouched, since the shipped
    // INIs hold Korean text in a legacy code page.
    private static readonly string[] AddressKeys = ["m_strMMOIP=", "m_strShopServerIP="];

    public byte[]? RenderConfig(string file, string mmoIp, bool korean = false, bool gm = false)
    {
        if (!file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) return null;
        string path = Locate(file, korean, gm);
        if (!File.Exists(path)) return null;
        byte[] bytes = File.ReadAllBytes(path);
        string text = Encoding.Latin1.GetString(bytes);
        bool changed = false;
        foreach (string key in AddressKeys)
        {
            int at = text.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) continue;
            changed = true;
            var output = new StringBuilder(text.Length);
            int position = 0;
            while (at >= 0)
            {
                int end = text.IndexOfAny(['\r', '\n'], at);
                if (end < 0) end = text.Length;
                output.Append(text, position, at - position).Append(key).Append(mmoIp);
                position = end;
                at = text.IndexOf(key, end, StringComparison.Ordinal);
            }
            output.Append(text, position, text.Length - position);
            text = output.ToString();
        }
        return changed ? Encoding.Latin1.GetBytes(text) : null;
    }

    private bool TryGetCrc(string file, out long length, out uint crc, bool korean = false, bool gm = false)
    {
        var info = new FileInfo(Locate(file, korean, gm));
        (length, crc) = (0, 0);
        if (!info.Exists) return false;
        length = info.Length;
        if (_crcs.TryGetValue(info.FullName, out var cached) && cached.Length == info.Length
            && cached.Written == info.LastWriteTimeUtc)
        {
            crc = cached.Crc;
            return true;
        }
        using (var stream = info.OpenRead()) crc = Crc32(stream);
        _crcs[info.FullName] = (info.Length, info.LastWriteTimeUtc, crc);
        Interlocked.Exchange(ref _crcsDirty, 1);
        return true;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static uint Crc32(ReadOnlySpan<byte> data) => ~UpdateCrc(0xFFFFFFFFu, data);

    private static uint Crc32(Stream stream)
    {
        uint crc = 0xFFFFFFFFu;
        byte[] buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) crc = UpdateCrc(crc, buffer.AsSpan(0, read));
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private async Task AcceptLoop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(stop);
                IPAddress address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address is { } remote
                    ? remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote
                    : IPAddress.None;
                bool admitted = TryAdmit(address);
                _ = Task.Run(() =>
                {
                    if (!admitted) { RefuseBusy(client); return; }
                    try { Respond(client); }
                    finally { Release(address); }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { _logger.LogWarning("[Patch] HTTP accept failed: {0}", ex.Message); }
        }
    }

    private bool TryAdmit(IPAddress address)
    {
        if (Interlocked.Increment(ref _active) > MaxConnections)
        {
            Interlocked.Decrement(ref _active);
            return false;
        }
        if (_activeByAddress.AddOrUpdate(address, 1, (_, count) => count + 1) > MaxConnectionsPerAddress)
        {
            Release(address);
            return false;
        }
        return true;
    }

    private void Release(IPAddress address)
    {
        Interlocked.Decrement(ref _active);
        if (_activeByAddress.AddOrUpdate(address, 0, (_, count) => count - 1) <= 0)
            _activeByAddress.TryRemove(new KeyValuePair<IPAddress, int>(address, 0));
    }

    private void RefuseBusy(TcpClient client)
    {
        using (client)
        try
        {
            client.SendTimeout = 1_000;
            _logger.LogInformation("[Patch] {0} refused: busy ({1} transfers running).", client.Client.RemoteEndPoint, _active);
            WriteText(client.GetStream(), 503, "Busy, try again", "text/plain", DateTime.UtcNow);
        }
        catch (Exception) { }
    }

    private void Respond(TcpClient client)
    {
        string path = "";
        var started = DateTime.UtcNow;
        var remoteEndPoint = client.Client.RemoteEndPoint;
        using (client)
        try
        {
            client.ReceiveTimeout = IoTimeoutMilliseconds;
            client.SendTimeout = SendTimeoutMilliseconds;
            NetworkStream stream = client.GetStream();
            string[] headers = ReadHeaders(stream, IoTimeoutMilliseconds).Split("\r\n");
            started = DateTime.UtcNow;
            string[] parts = headers[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            path = parts.Length >= 2 ? Uri.UnescapeDataString(parts[1].Split('?')[0]) : "/";
            string mmoIp = MmoAddress(Environment.GetEnvironmentVariable("HUXLEY_GAME_PUBLIC_IP"),
                (client.Client.LocalEndPoint as IPEndPoint)?.Address);
            string? acceptLanguage = headers.Skip(1)
                .FirstOrDefault(line => line.StartsWith("Accept-Language:", StringComparison.OrdinalIgnoreCase))?
                ["Accept-Language:".Length..].Trim();
            bool korean = IsKorean(acceptLanguage);
            if (acceptLanguage is { Length: > 0 } && client.Client.RemoteEndPoint is IPEndPoint remote)
                ReportLanguage(remote.Address, korean);
            bool gm = _gm.Count > 0 && client.Client.RemoteEndPoint is IPEndPoint caller && IsGm(caller.Address);

            if (path.Equals(PatchDir + "LauncherVersion.txt", StringComparison.OrdinalIgnoreCase))
                WriteText(stream, 200, $"{LauncherVersion} {LauncherVersion} none 0\r\n", "text/plain", started);
            else if (path.Equals(PatchDir + "Version_2.txt", StringComparison.OrdinalIgnoreCase))
                WriteText(stream, 200, VersionFile(gm), "text/plain", started);
            else if (path.Equals(PatchDir + "NetTestDummyFile.dmy", StringComparison.OrdinalIgnoreCase))
                WriteBytes(stream, 200, new byte[4 << 20], "application/octet-stream", started);
            else if (path.Equals(FullDir + "FullFile.txt", StringComparison.OrdinalIgnoreCase))
            {
                WriteText(stream, 200, FullFileList(mmoIp, korean, gm), "text/plain", started);
                _logger.LogInformation("[Patch] {0} checked its files against version {1} ({2}{3}).",
                    client.Client.RemoteEndPoint, gm ? GmVersion() : CurrentVersion(), korean ? "Korean" : "English",
                    gm ? ", GM" : "");
            }
            else if (path.StartsWith(FullDir, StringComparison.OrdinalIgnoreCase)
                && _byPath.TryGetValue(path[FullDir.Length..].Replace('/', '\\'), out string? file)
                && Serves(file, korean))
            {
                if (RenderConfig(file, mmoIp, korean, gm) is { } rendered)
                    WriteBytes(stream, 200, rendered, "application/octet-stream", started);
                else
                    WriteFile(stream, Locate(file, korean, gm), started);
            }
            else if (path.Equals("/notice", StringComparison.OrdinalIgnoreCase) || path == "/")
                WriteText(stream, 200, NoticeHtml, "text/html", started);
            else
                WriteText(stream, 404, "Not found", "text/plain", started);
        }
        catch (IOException ex)
        {
            // The launcher reports any failed file as "cannot reach the update server"; say which and why.
            if (path.StartsWith(FullDir, StringComparison.OrdinalIgnoreCase))
                _logger.LogInformation("[Patch] {0} stopped receiving {1} after {2:0}s: {3}", remoteEndPoint,
                    path[FullDir.Length..], (DateTime.UtcNow - started).TotalSeconds, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex) { _logger.LogWarning("[Patch] HTTP request failed: {0}", ex.Message); }
    }

    // The Agent the client logs in to shares the game server's address, so the configured public
    // address wins over the interface that accepted the launcher.
    public static string MmoAddress(string? configuredAddress, IPAddress? localAddress) =>
        Agent.ResolveGameAddress(configuredAddress, localAddress ?? IPAddress.Loopback).ToString();

    private static string ReadHeaders(NetworkStream stream, int timeoutMilliseconds)
    {
        var bytes = new List<byte>();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (bytes.Count < 16384)
        {
            long remaining = timeoutMilliseconds - elapsed.ElapsedMilliseconds;
            if (remaining <= 0)
                throw new IOException("request headers too slow");
            // Each read waits only for what is left of the total deadline.
            stream.ReadTimeout = (int)remaining;
            int b = stream.ReadByte();
            if (b < 0) break;
            bytes.Add((byte)b);
            int count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n'
                && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
                break;
        }
        return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r', '\n');
    }

    private static void WriteHeader(Stream stream, int status, long length, string contentType)
    {
        string reason = status switch { 200 => "OK", 503 => "Service Unavailable", _ => "Not Found" };
        byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.0 {status} {reason}\r\nContent-Type: {contentType}\r\n" +
            $"Content-Length: {length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
        stream.Write(header);
    }

    private static void WriteText(Stream stream, int status, string body, string contentType, DateTime started) =>
        WriteBytes(stream, status, Encoding.UTF8.GetBytes(body), contentType + "; charset=utf-8", started);

    private static void WriteBytes(Stream stream, int status, byte[] body, string contentType, DateTime started)
    {
        WriteHeader(stream, status, body.Length, contentType);
        DateTime deadline = BodyDeadline(started, body.Length);
        // Chunked, so the send timeout bounds a stalled peer rather than the whole 4 MB body.
        for (int offset = 0; offset < body.Length; offset += ChunkSize)
        {
            CheckDeadline(deadline);
            stream.Write(body, offset, Math.Min(ChunkSize, body.Length - offset));
        }
    }

    private static void WriteFile(Stream stream, string path, DateTime started)
    {
        using var file = File.OpenRead(path);
        WriteHeader(stream, 200, file.Length, "application/octet-stream");
        DateTime deadline = BodyDeadline(started, file.Length);
        byte[] buffer = new byte[ChunkSize];
        int read;
        while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
        {
            CheckDeadline(deadline);
            stream.Write(buffer, 0, read);
        }
    }

    // A body that drains slower than MinBytesPerSecond (plus 30 s of slack) frees its slot.
    internal static DateTime BodyDeadline(DateTime started, long length) =>
        started.AddSeconds(30 + (double)length / MinBytesPerSecond);

    private static void CheckDeadline(DateTime deadline)
    {
        if (DateTime.UtcNow > deadline) throw new IOException("response too slow");
    }

    private const string NoticeHtml = """
        <!doctype html><html><head><meta charset="utf-8"><title>Huxley</title>
        <style>body{margin:0;background:#101418;color:#d8dde3;font:14px Tahoma,Arial,sans-serif}
        div{padding:28px 34px}h1{margin:0 0 12px;font-size:22px;color:#f0b44c}p{margin:6px 0;line-height:1.5}</style>
        </head><body><div><h1>Huxley private server</h1>
        <p>The launcher checks your game files and downloads anything that changed.</p>
        <p>When it says the update is complete, press Start.</p></div></body></html>
        """;

    public void Dispose()
    {
        _reloadTimer?.Dispose();
        _stop?.Cancel();
        _listener?.Stop();
    }
}
