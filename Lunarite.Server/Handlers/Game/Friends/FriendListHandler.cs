using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class FriendListHandler
{
    private static CharacterManager _characterManager = null!;
    private static CharacterStore _characterStore = null!;
    private static FriendStore _friendStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _friendStore = serviceProvider.GetRequiredService<FriendStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _characterManager = serviceProvider.GetRequiredService<CharacterManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var response = new FriendList();
        foreach (var entry in _friendStore.List(pid))
        {
            var character = _characterStore.Get(entry.Pid);
            if (character == null) continue;
            var progress = _playerStore.GetProgress(entry.Pid);
            response.Entries.Add(new FriendWireEntry(entry.Relation, entry.Pid, character.Name,
                (byte)character.Index, (byte)Math.Clamp(progress.Level, 0, 255), character.Style,
                _characterManager.IsPlayerOnline(entry.Pid) ? (byte)1 : (byte)0));
        }
        client.SendPacket(response);
        return true;
    }
}
