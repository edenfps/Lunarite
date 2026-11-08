using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

using Lunarite.Storage;

namespace Lunarite.World;

// Players online per faction for outside status tools (HUXLEY_STATUS_FILE, see Program.cs). Character
// TypeIndex below 10 is a Sapiens character, 10 and above an Alternative (CharacterStore.SimulationCenterMap).
public static class OnlineStatus
{
    public static (List<string> Sapiens, List<string> Alternative) Names(PlayerManager players, CharacterStore characters)
    {
        List<string> sapiens = [], alternative = [];
        foreach (int pid in players.OnlinePids)
        {
            if (characters.Get(pid) is not { } character) continue;
            (character.Index >= 10 ? alternative : sapiens).Add(DisplayName(character.Name));
        }
        sapiens.Sort(StringComparer.OrdinalIgnoreCase);
        alternative.Sort(StringComparer.OrdinalIgnoreCase);
        return (sapiens, alternative);
    }

    // Names are stored byte for byte as the client sent them (PacketReader: one char per byte), in the
    // player's Windows code page. Outside the game they need real text: CP949 (Korean) when the bytes
    // are valid CP949, else CP1252 (Western).
    private static readonly Encoding Cp949, Cp1252;

    static OnlineStatus()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        Cp1252 = Encoding.GetEncoding(1252);
    }

    public static string DisplayName(string stored)
    {
        if (stored.All(c => c < 0x80)) return stored;
        if (stored.Any(c => c > 0xFF)) return stored;
        byte[] bytes = Encoding.Latin1.GetBytes(stored);
        try { return Cp949.GetString(bytes); }
        catch (DecoderFallbackException) { return Cp1252.GetString(bytes); }
    }

    // inMission counts players in a quest or battle: a restart waits for them, not for those in a city or camp.
    public static string Json(IReadOnlyList<string> sapiens, IReadOnlyList<string> alternative, int inMission, long unixSeconds) =>
        $"{{\"online\":{sapiens.Count + alternative.Count},\"sapiens\":{sapiens.Count},\"alternative\":{alternative.Count},\"sapiensNames\":{JsonSerializer.Serialize(sapiens)},\"alternativeNames\":{JsonSerializer.Serialize(alternative)},\"inMission\":{inMission},\"updated\":{unixSeconds}}}\n";

    public static void Write(string path, PlayerManager players, CharacterStore characters, Func<int, bool> inMission)
    {
        try
        {
            var (sapiens, alternative) = Names(players, characters);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, Json(sapiens, alternative, players.OnlinePids.Count(inMission),
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            File.Move(temporary, path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // notice.txt next to the status file is a server-wide notice from outside the game (restart
    // announcements): taken once, then gone.
    public static string NoticePath(string statusFile) =>
        Path.Combine(Path.GetDirectoryName(statusFile) ?? "", "notice.txt");

    public static string? TakeNotice(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path).Trim();
            File.Delete(path);
            return text.Length == 0 ? null : text;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
