using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// Every shipped quest has ResultDrop=false, so the script's RequestQuestResult path is unused;
// native RequestQuestResult stores a delegate and sends nothing. Client op62 `db` also
// carries listen diagnostics. No result rewards are generated.
[PacketHandler]
public static class DiagnosticsResultHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;
    private static PlayerManager _playerManager = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(DiagnosticsResultHandler));

        _playerManager = serviceProvider.GetRequiredService<PlayerManager>();
        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!reader.TryRead(out int questIndex) || !reader.TryRead(out byte callback)
            || reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        // A live PID in field one means diagnostics; retail quest IDs are 8xxxxx.
        if (_playerManager.IsOnline(questIndex))
        {
            if (questIndex == pid)
            {
                int currentInstance = _questRoomMembers.InstanceOf(client.Id);
                if (currentInstance > 0 && _questInstanceManager.TryGetHost(currentInstance, out int hostPid)
                    && hostPid != pid && Game.TryGetClient(hostPid, out var hostClient))
                    hostClient.SendPacket(new ListenDiagnostics { ClientPid = pid, ErrorCode = callback });
                else
                    client.SendPacket(new ListenDiagnostics { ClientPid = pid, ErrorCode = 1 });
            }
            else if (Game.TryGetClient(questIndex, out var requester)
                && _questRoomMembers.InstanceOf(requester.Id) > 0
                && _questRoomMembers.InstanceOf(requester.Id) == _questRoomMembers.InstanceOf(client.Id))
                requester.SendPacket(new ListenDiagnostics { ClientPid = questIndex, ErrorCode = callback });
            return true;
        }
        bool acquired = pid > 0 && _questStore.Acquired(pid).Contains(questIndex);
        var progress = _questStore.GetProgress(pid, questIndex);
        byte rank = !acquired ? (byte)6 : progress.Complete == 2 ? (byte)1 : (byte)0;
        client.SendPacket(new ReceivedQuestResult { Rank = rank });
        _logger.LogInformation("[Quests] result request PID {0}, quest {1}, callback {2}: rank {3}.",
            pid, questIndex, callback, rank);
        return true;
    }
}
