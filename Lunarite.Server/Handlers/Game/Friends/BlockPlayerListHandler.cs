using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.Handlers;

[PacketHandler]
public static class BlockPlayerListHandler
{
    private static BlockStore _blockStore = null!;
    private static CharacterStore _characterStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _blockStore = serviceProvider.GetRequiredService<BlockStore>();
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var response = new BlockPlayerList { Result = 0 };
        foreach (int targetPid in _blockStore.List(pid))
        {
            var character = _characterStore.Get(targetPid);
            if (character == null) continue;
            var progress = _playerStore.GetProgress(targetPid);
            response.Entries.Add(new BlockWireEntry(targetPid, character.Name,
                (byte)Math.Clamp(progress.Level, 0, 255), character.Style, (byte)character.Index));
        }
        client.SendPacket(response);
        return true;
    }
}
