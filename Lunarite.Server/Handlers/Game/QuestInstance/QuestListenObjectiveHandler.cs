using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;
using Lunarite.World;

namespace Lunarite.Handlers;

// SendQuestListenObjective(int PID, int Quest, int Objective, int Count, bool Visible, delegate).
// Persisted so op37 restores it after reconnect.
[PacketHandler]
public static class QuestListenObjectiveHandler
{
    private static QuestRoomMembers _questRoomMembers = null!;
    private static QuestManager _questManager = null!;
    private static QuestInstanceManager _questInstanceManager = null!;
    private static ILogger _logger = null!;
    private static QuestStore _questStore = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(QuestListenObjectiveHandler));

        _questStore = serviceProvider.GetRequiredService<QuestStore>();
        _questInstanceManager = serviceProvider.GetRequiredService<QuestInstanceManager>();
        _questManager = serviceProvider.GetRequiredService<QuestManager>();
        _questRoomMembers = serviceProvider.GetRequiredService<QuestRoomMembers>();
    }

    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        if (!QuestListenObjectiveRequest.TryDeserialize(ref reader, out var packet))
        {
            _logger.LogWarning("QuestListenObjective: malformed payload {0}.", Convert.ToHexString(reader.Span));
            return false;
        }

        int selectedPid = Game.PidOf(client);
        // City Kismet also sends op55 without a room (e.g. SeqAct_QuestAcquire's Completed
        // branch); that is accepted only for an acquired non-room city quest.
        bool acquiredByReporter = _questStore.Acquired(selectedPid).Contains(packet.QuestIndex);
        int ownerPid = _questManager.IsCityObjectiveReportAuthorized(selectedPid, packet.Pid,
                packet.QuestIndex, acquiredByReporter)
            || _questManager.IsFreeQuestObjectiveReportAuthorized(selectedPid, packet.Pid,
                _questRoomMembers.QuestOf(client.Id), _questRoomMembers.InstanceOf(client.Id),
                packet.QuestIndex, acquiredByReporter)
            ? selectedPid : _questInstanceManager.ResolveQuestReportOwner(client, packet.Pid, packet.QuestIndex);
        bool valid = ownerPid > 0 && packet.ObjectiveIndex < 8
            && _questStore.UpdateObjective(ownerPid, packet.QuestIndex, packet.ObjectiveIndex, packet.Count, packet.Visible != 0);
        if (!valid)
        {
            _logger.LogWarning("QuestListenObjective rejected: PID={0} selected={1} quest={2} objective={3} count={4} visible={5}.",
                packet.Pid, selectedPid, packet.QuestIndex, packet.ObjectiveIndex, packet.Count, packet.Visible);
        }
        else
        {
            _questInstanceManager.CommitQuestEntryOnReward(_questRoomMembers.InstanceOf(client.Id), ownerPid);
            _logger.LogInformation("[Quests] PID {0} quest {1} objective {2} = {3} (visible={4}).",
                ownerPid, packet.QuestIndex, packet.ObjectiveIndex, packet.Count, packet.Visible != 0);
        }
        client.SendPacket(new QuestProgressAnswer { Opcode = 58, Answer = valid ? (byte)0 : (byte)1,
            QuestIndex = packet.QuestIndex, Index = packet.ObjectiveIndex, Count = packet.Count,
            State = packet.Visible });
        return true;
    }
}
