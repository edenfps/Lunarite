using System;

using Microsoft.Extensions.DependencyInjection;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

[PacketHandler]
public static class SquadJoinHandler
{
    private static SquadManager _squadManager = null!;
    private static CharacterStore _characterStore = null!;
    private static PlayerStore _playerStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        _characterStore = serviceProvider.GetRequiredService<CharacterStore>();
        _playerStore = serviceProvider.GetRequiredService<PlayerStore>();
        _squadManager = serviceProvider.GetRequiredService<SquadManager>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int key) || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        var (answer, leaderPid) = _squadManager.SquadJoin(pid, key);
        if (answer != 0)
            client.SendPacket(new SquadActionResult { Opcode = 102, Answer = answer });
        else if (Game.TryGetClient(leaderPid, out var leaderClient))
        {
            var character = _characterStore.Get(pid);
            var progress = _playerStore.GetProgress(pid);
            leaderClient.SendPacket(new SquadApplication { Pid = pid, Name = character?.Name ?? "",
                Style = character?.Style ?? 0, Level = (ushort)Math.Clamp(progress.Level, 0, ushort.MaxValue),
                SquadKey = key });
        }
        else
        {
            // The leader left before the prompt could be delivered.
            _squadManager.WithdrawApplication(pid, key);
            client.SendPacket(SquadManager.BuildSquadJoinFailure());
        }
        return true;
    }
}
