using System;
using System.IO;
using System.Linq;

using Lunarite.Storage;

namespace Lunarite.World;

// Players online per faction for outside status tools (HUXLEY_STATUS_FILE, see Program.cs). Character
// TypeIndex below 10 is a Sapiens character, 10 and above an Alternative (CharacterStore.SimulationCenterMap).
public static class OnlineStatus
{
    public static (int Sapiens, int Alternative) Count(PlayerManager players, CharacterStore characters)
    {
        int sapiens = 0, alternative = 0;
        foreach (int pid in players.OnlinePids)
        {
            if (characters.Get(pid) is not { } character) continue;
            if (character.Index >= 10) alternative++;
            else sapiens++;
        }
        return (sapiens, alternative);
    }

    public static string Json(int sapiens, int alternative, long unixSeconds) =>
        $"{{\"online\":{sapiens + alternative},\"sapiens\":{sapiens},\"alternative\":{alternative},\"updated\":{unixSeconds}}}\n";

    public static void Write(string path, PlayerManager players, CharacterStore characters)
    {
        try
        {
            var (sapiens, alternative) = Count(players, characters);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, Json(sapiens, alternative, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            File.Move(temporary, path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
