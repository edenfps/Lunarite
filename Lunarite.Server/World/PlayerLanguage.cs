using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Lunarite.Network;

namespace Lunarite.World;

// Korean or English for the server's own messages to a player. The launcher's language wins: it
// tells the patch server (Accept-Language) from the player's address. A player the launcher has not
// reported since the last restart reads as Korean when the address is in a Korean (KR) block of
// APNIC's public delegation list, else English.
public static class PlayerLanguage
{
    public const string AddressListUrl = "https://ftp.apnic.net/stats/apnic/delegated-apnic-latest";
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(7);
    private static volatile (uint Start, uint End)[] _korean = [];

    public static bool IsKorean(Client client) => IsKorean(client.IPAddress);

    public static bool IsKorean(IPAddress address) =>
        PatchServer.ReportedLanguage(address) is { } reported ? reported == "ko" : InKoreanBlock(address);

    public static string Pick(Client client, string english, string korean) => IsKorean(client) ? korean : english;

    internal static bool InKoreanBlock(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        uint value = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
        var blocks = _korean;
        int low = 0, high = blocks.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (value < blocks[middle].Start) high = middle - 1;
            else if (value > blocks[middle].End) low = middle + 1;
            else return true;
        }
        return false;
    }

    // Lines "apnic|KR|ipv4|<first address>|<address count>|..." of the delegation file.
    internal static (uint Start, uint End)[] ParseKoreanBlocks(IEnumerable<string> lines)
    {
        var blocks = new List<(uint, uint)>();
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (parts.Length < 5 || parts[1] != "KR" || parts[2] != "ipv4"
                || !IPAddress.TryParse(parts[3], out IPAddress? first)
                || !uint.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out uint count) || count == 0)
                continue;
            uint start = BinaryPrimitives.ReadUInt32BigEndian(first.GetAddressBytes());
            blocks.Add((start, start + (count - 1)));
        }
        return blocks.OrderBy(block => block.Item1).ToArray();
    }

    internal static void UseKoreanBlocks((uint Start, uint End)[] blocks) => _korean = blocks;

    // Reads the cached list (cachePath) and fetches a fresh one when it is missing or a week old.
    // HUXLEY_KOREAN_ADDRESSES=0 turns the lookup off.
    public static void Start(string cachePath, ILogger logger)
    {
        if (Environment.GetEnvironmentVariable("HUXLEY_KOREAN_ADDRESSES") == "0") return;
        if (File.Exists(cachePath)) LoadCache(cachePath, logger);
        if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < RefreshAfter) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                string text = await http.GetStringAsync(AddressListUrl);
                var korean = text.Split('\n').Where(line => line.StartsWith("apnic|KR|ipv4|", StringComparison.Ordinal));
                await File.WriteAllLinesAsync(cachePath + ".tmp", korean);
                File.Move(cachePath + ".tmp", cachePath, true);
                LoadCache(cachePath, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning("[Language] Korean address list not refreshed: {0}", ex.Message);
            }
        });
    }

    private static void LoadCache(string cachePath, ILogger logger)
    {
        try
        {
            UseKoreanBlocks(ParseKoreanBlocks(File.ReadLines(cachePath)));
            logger.LogInformation("[Language] {0} Korean address blocks loaded.", _korean.Length);
        }
        catch (IOException ex) { logger.LogWarning("[Language] Korean address list unreadable: {0}", ex.Message); }
    }
}
