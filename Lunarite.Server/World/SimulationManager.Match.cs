using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Incoming;
using Lunarite.Packets.Outgoing;

namespace Lunarite.World;

// A started Simulation match: the listen host, member permissions and travel, the host's
// reports while it runs, and the return to the room.
public sealed partial class SimulationManager
{

    // Periodic battle recovery data from the listen host, client op152 `wwwbm`: two team
    // scores, remaining seconds, mode-specific backup type, then 31-byte player records.
    // `m` has no count prefix.
    internal void BattleGameData(Client client, BattleGameDataRequest report)
    {
        int ownerPid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!_simulationTransports.TryGetValue(ownerPid, out var transport)
                || transport.OwnerPid != ownerPid
                || !transport.Started
                || report.PlayerCount > transport.Members.Count)
                return;
            transport.TeamScore0 = report.TeamScore0;
            transport.TeamScore1 = report.TeamScore1;
            transport.RemainingTime = report.RemainingTime;
            transport.BackupType = report.BackupType;
            transport.BattleGameData = report.PlayerRecords;
            _logger.LogDebug(
                "[Simulation] room {0} battle backup: score {1}:{2}, remaining {3}, type {4}, players {5}.",
                transport.RoomId, report.TeamScore0, report.TeamScore1, report.RemainingTime,
                report.BackupType, report.PlayerCount);
        }
    }

    internal void BattleResultSecret(Client client)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(pid, out var room, out _) || room.OwnerPid != pid)
                return;
            FinishSimulationMatchLocked(room);
            _logger.LogInformation("[Simulation] room {0} match finished; returned to READY.", room.Id);
        }
    }

    // The listen host reports a guest's completed UE3/P2P login as client op109
    // `db` (PID, SC_PLAYER_STATE_*). Retail reflects it to room UIs through
    // server op142 / OnSimulPlayerStateBusy.
    internal void SimulationJoinedPlayerState(Client client, int memberPid, byte state)
    {
        int ownerPid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!_simulationTransports.TryGetValue(ownerPid, out var transport)
                || transport.OwnerPid != ownerPid
                || !_simulationRooms.TryGetValue(transport.RoomId, out var room)
                || !room.Members.TryGetValue(memberPid, out var member))
                return;
            if (state is not (0 or 1 or 2 or 4)) return;
            member.State = state;
            if (IsSimulationGuestAdmissionComplete(state))
            {
                transport.Confirmed.Add(memberPid);
                transport.Pending.Remove(memberPid);
                transport.JoinedAtSeconds.TryAdd(memberPid, transport.StartedAtUtc is { } start
                    ? Math.Max(0, (int)(DateTime.UtcNow - start).TotalSeconds) : 0);
            }
            BroadcastSimulationLocked(room, new SimulPlayerStateBusy { Pid = memberPid, State = state });
            _logger.LogInformation("[Simulation] room {0} host PID {1} reported PID {2} state {3}.",
                transport.RoomId, ownerPid, memberPid, state);
        }
    }

    internal void SimulationPermissionAck(Client client, int memberPid, byte result)
    {
        int ownerPid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!_simulationTransports.TryGetValue(ownerPid, out var transport)) return;
            _logger.LogInformation("[Simulation] room {0} host PID {1} permission ACK for PID {2}: success={3}.",
                transport.RoomId, ownerPid, memberPid, result != 0);
            if (result == 0 || !transport.Pending.Remove(memberPid)) return;
            // op156 right after the ACK lets members join during the pre-match countdown. A member
            // that arrives while the owner is loading resends HELLO until the listen answers.
            SendSimulationJoinMatchLocked(transport, memberPid);
        }
    }

    // FP2PSocket reports a completed room-peer probe as client op194 `bd`:
    // PingType (0/1 for Simulation peers) and the computed grade (0..3; 4 is
    // the native unknown/unreachable sentinel). Retail forwards the resulting
    // PID/grade table to the room owner through server op224 OwnerPingTime.
    internal void SimulationPingReport(Client client, byte pingType, int pingGrade)
    {
        if (pingType > 1 || pingGrade is < 0 or > 4) return;
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(pid, out var room, out var member)) return;
            member.PingGrade = pingGrade;
            if (!Game.TryGetClient(room.OwnerPid, out var ownerClient)) return;
            var update = new OwnerPingTime();
            foreach (var peer in room.Members.Values.Where(x => x.Pid != room.OwnerPid))
                update.Entries.Add(new SimulationPing(peer.Pid, peer.PingGrade));
            ownerClient.SendPacket(update);
            _logger.LogDebug("[SimulationCenter] P2P ping room {0}: PID {1}, type {2}, grade {3}.",
                room.Id, pid, pingType, pingGrade);
        }
    }

    // HuxleyGame.MatchInProgress.BeginState sends this once the listen host has begun the match.
    // It is a status report, not a request to launch.
    internal void StartMatch(Client client)
    {
        int pid = Game.PidOf(client);
        // Quest listen hosts send op141 about a second after their map loads. op199 only comes
        // from a 30-second timer, so waiting co-op guests are dispatched here.
        if (_questInstanceManager.HasTransport(pid))
        {
            _questInstanceManager.ConfirmQuestHostReady(pid, _questRoomMembers.QuestOf(client.Id),
                QuestInstanceManager.QuestTransportSignal.HostRoomLive);
            return;
        }
        lock (_simulationLock)
        {
            if (!_simulationTransports.TryGetValue(pid, out var transport)
                || transport.OwnerPid != pid) return;
            transport.Started = true;
            transport.StartedAtUtc ??= DateTime.UtcNow;
            _logger.LogInformation("[Simulation] room {0} listen host PID {1} entered MatchInProgress.",
                transport.RoomId, pid);
            // The owner's P2P receive thread answers hole punches while the map loads, so it is
            // suspended only now.
            client.SendPacket(Game.P2PDirectHoleResult);
        }
    }

    // restore: the transport of a running match whose host was lost. Its last op152 backup
    // (team scores, remaining time, each player's battle record) goes to the new host, and the
    // room's start checks are skipped because the match is already under way.
    private void BeginSimulationMatchLocked(Client client, int pid, SCRoomInfo room,
        SimulationTransport? restore = null)
    {
        if (restore == null && room.OwnerPid != pid)
        {
            client.SendPacket(new OwnerStart { Result = SimulationAnswer.Error });
            return;
        }
        if (restore == null && room.Members.Values.Any(x => x.Pid != pid && !x.Ready))
        {
            client.SendPacket(new OwnerStart { Result = SimulationAnswer.NoReady });
            return;
        }
        // Sides are the two factions (Tribe One/Syn vs Alterlabor/Alternix). Same start rule
        // as the room menu (SC_ANSWER_PLAYER_PROPORTION).
        if (restore == null
            && !CheckStartCondition(room.GameType, room.MaxPlayers, room.Members.Values.Select(x => x.Species)))
        {
            client.SendPacket(new OwnerStart { Result = SimulationAnswer.PlayerProportion });
            return;
        }
        var zone = _battleZoneCatalog.Get(room.Map);
        if (zone == null)
        {
            client.SendPacket(new OwnerStart { Result = SimulationAnswer.Error });
            return;
        }
        // A relay guest promoted after host loss must stop wrapping its traffic for the relay.
        _p2pManager.StopRelay(client);
        byte[] key = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        // HostIp is the UE3 browse address (like the quest path's hostJoinIp): the TCP source
        // of the owner's game connection, or SIMULATION_HOST_IP behind a port-forwarded NAT.
        string hostAddress = Environment.GetEnvironmentVariable("SIMULATION_HOST_IP")
            ?? client.IPAddress.MapToIPv4().ToString();
        uint ownerJoinIp = P2PManager.PackedIp(hostAddress);
        var transport = new SimulationTransport
        {
            RoomId = room.Id, OwnerPid = pid, Members = room.Members.Keys.ToList(),
            Pending = room.Members.Keys.Where(x => x != pid).ToHashSet(),
            Key = key, HostIp = unchecked((int)ownerJoinIp),
            TimeLimitSeconds = 60 * (room.GoalTime != 0 ? room.GoalTime : zone.TimeLimitMinutes),
            // Members hole-punch to the owner's P2P socket before travel (op128), as
            // mission guests do.
            PeerEndpoints = P2PManager.BuildClientPeerEndpoints(client,
                Environment.GetEnvironmentVariable("SIMULATION_HOST_IP"), ownerJoinIp)
        };
        if (restore != null)
        {
            transport.TeamScore0 = restore.TeamScore0;
            transport.TeamScore1 = restore.TeamScore1;
            transport.RemainingTime = restore.RemainingTime;
            transport.BattleGameData = restore.BattleGameData;
            transport.StartedAtUtc = restore.StartedAtUtc;
            transport.TimeLimitSeconds = restore.TimeLimitSeconds;
            transport.JoinedAtSeconds = new Dictionary<int, int>(restore.JoinedAtSeconds);
        }
        _simulationTransports[pid] = transport;
        room.State = 1;
        foreach (var member in room.Members.Values)
        {
            member.State = 2; // SC_PLAYER_STATE_PLAY
            BroadcastSimulationLocked(room, new ChangePlayerStateBC
                { Pid = member.Pid, State = member.State });
        }
        if (restore == null) client.SendPacket(new OwnerStart { Result = SimulationAnswer.Ok });
        // op155 does not arm the owner's P2P socket (only op42, op124 and op128 do), and an
        // unarmed listen world binds plain UDP that the members cannot reach. op128 arms it.
        int firstMember = transport.Pending.FirstOrDefault();
        if (firstMember > 0 && Game.TryGetClient(firstMember, out var firstMemberClient))
            client.SendPacket(P2PManager.BuildClientPeerEndpoints(firstMemberClient, null,
                P2PManager.PackedIp(firstMemberClient.IPAddress.MapToIPv4().ToString())));
        client.SendPacket(new SimulationCreateMatch
        {
            Map = _simulationMapOverrides.LevelFor(room.Map) ?? zone.LevelName,
            Key = key,
            MinPlayers = 1,
            GameType = room.GameType,
            TimeLimit = (ushort)(transport.TimeLimitSeconds / 60),
            // 0 is the room menu's "infinite" score (the INI default), not "use the map's".
            GoalScore = room.GoalScore,
            SecretRoom = room.IsPublic == 0 ? (byte)1 : (byte)0,
            TeamScore0 = transport.TeamScore0,
            TeamScore1 = transport.TeamScore1,
            RemainingTime = transport.RemainingTime,
            Player = BuildSimulationPlayerState(pid, room, key, BattleBackupOf(transport, pid))
        });
        // Order: op155, one op157 per guest, then op156 once the host acknowledges that
        // guest's permission.
        foreach (int memberPid in transport.Pending.ToArray())
        {
            SendSimulationPermissionRegisterLocked(transport, memberPid);
        }
        BroadcastSimulationChannelLocked(room.Channel, new ChangeGameStateBC
            { RoomId = room.Id, GameState = room.State });
        BroadcastSimulationChannelLocked(room.Channel, BuildSimulationRoomBroadcast(room));
        _logger.LogInformation("[Simulation] started room {0} host PID {1}, map '{2}', members [{3}], IP {4}.",
            room.Id, pid, zone.LevelName, string.Join(",", transport.Members), hostAddress);
    }

    private void SendSimulationJoinMatchLocked(SimulationTransport transport, int memberPid)
    {
        if (!Game.TryGetClient(memberPid, out var memberClient)) return;
        var join = new SimulationJoinMatch
        {
            HostIp = unchecked((int)P2PManager.HostIpFor(unchecked((uint)transport.HostIp), memberClient)),
            Key = transport.Key
        };
        _logger.LogInformation("[Simulation] room {0} joining PID {1} after permission ACK (host-started={2}).",
            transport.RoomId, memberPid, transport.Started);
        if (transport.PeerEndpoints == null)
        {
            memberClient.SendPacket(join);
            return;
        }
        // A member re-admitted after host loss still relays to the old host.
        _p2pManager.StopRelay(memberClient);
        int ownerPid = transport.OwnerPid;
        var hostEndpoints = P2PManager.HostEndpointsFor(transport.PeerEndpoints, memberClient);
        // As on missions: the owner gets this member's endpoints too, so its socket punches back.
        void Probe()
        {
            if (Game.TryGetClient(ownerPid, out var owner))
                owner.SendPacket(P2PManager.HostEndpointsFor(P2PManager.BuildClientPeerEndpoints(memberClient, null,
                    P2PManager.PackedIp(memberClient.IPAddress.MapToIPv4().ToString())), owner));
            memberClient.SendPacket(hostEndpoints);
        }
        memberClient.Session.HolePunchAcknowledged = false;
        Probe();
        _ = _p2pManager.SendAfterHolePunch(memberClient, memberPid, ownerPid, join, "op156", Probe,
            () => Game.TryGetClient(ownerPid, out var relayHost) && _p2pManager.TryStartRelay(memberClient, ownerPid,
                relayHost, hostEndpoints.PublicEndpoint, hostEndpoints.PeerEndpoint),
            () => !IsAwaitingSimulationJoin(transport, memberPid));
    }

    // The punch can outlast the match or the member's stay in the room.
    private bool IsAwaitingSimulationJoin(SimulationTransport transport, int memberPid)
    {
        lock (_simulationLock)
            return _simulationTransports.TryGetValue(transport.OwnerPid, out var current)
                && ReferenceEquals(current, transport) && _simulationPidRoom.TryGetValue(memberPid, out byte roomId)
                && roomId == transport.RoomId;
    }

    private void SendSimulationPermissionRegisterLocked(SimulationTransport transport, int memberPid)
    {
        if (!transport.Pending.Contains(memberPid)
            || !Game.TryGetClient(transport.OwnerPid, out var ownerClient)
            || !_simulationRooms.TryGetValue(transport.RoomId, out var room)) return;
        ownerClient.SendPacket(new SimulationPermissionRegister
        {
            Player = BuildSimulationPlayerState(memberPid, room, transport.Key, BattleBackupOf(transport, memberPid))
        });
        _logger.LogInformation("[Simulation] room {0} registered fresh permission for PID {1} (host-started={2}).",
            transport.RoomId, memberPid, transport.Started);
    }

    // A player's 31-byte record in the last op152 backup; records start with the PID.
    private static byte[]? BattleBackupOf(SimulationTransport transport, int pid)
    {
        for (int offset = 0; offset + 31 <= transport.BattleGameData.Length; offset += 31)
            if (BitConverter.ToInt32(transport.BattleGameData, offset) == pid)
                return transport.BattleGameData[offset..(offset + 31)];
        return null;
    }

    private SimulationPlayerState BuildSimulationPlayerState(int pid, SCRoomInfo room, byte[] key,
        byte[]? battleBackup = null)
    {
        var character = _characterStore.Get(pid);
        var progress = _playerStore.GetProgress(pid);
        var member = room.Members.GetValueOrDefault(pid);
        return new SimulationPlayerState
        {
            Key = key.ToArray(), RoomId = room.Id, Squad = member?.Squad ?? 0, Pid = pid,
            Name = character?.Name ?? "", Character = (byte)(character?.Index ?? 6),
            Money = (int)Math.Clamp(progress.Money, int.MinValue, int.MaxValue),
            Appearance = character?.Custom ?? new Custom(), Level = progress.Level,
            Style = character?.Style ?? 0, Rank = progress.Rank,
            BP = (int)Math.Clamp(progress.BP, int.MinValue, int.MaxValue),
            LP = (int)Math.Clamp(progress.LP, int.MinValue, int.MaxValue), RespawnCount = progress.RespawnCount,
            InvenExpandStep = progress.InvenExpandStep, BankExpandStep = progress.BankExpandStep,
            DurativeItems = _durativeItemStore.Active(pid), PersonalBuffs = _personalBuffStore.Active(pid),
            Licenses = _licenseStore.Owned(pid).ToList(), Items = _itemManager.StartingLoadoutItems(pid),
            SkillSlots = _playerStore.GetSkillSlots(pid),
            BattleBackup = battleBackup
        };
    }

    private void FinishSimulationMatchLocked(SCRoomInfo room)
    {
        room.State = 0;
        foreach (var member in room.Members.Values)
        {
            member.Ready = false;
            member.State = 0;
            BroadcastSimulationLocked(room, new ChangePlayerStateBC
                { Pid = member.Pid, State = member.State });
        }
        // The play time is taken here: op115 ends the transport before the host's op114 results.
        // Only a match that reached MatchInProgress (op141) can be settled; a room started and
        // ended at once would otherwise mint rewards on demand, and it also closes the owner's
        // previous match.
        if (_simulationTransports.Remove(room.OwnerPid, out var transport) && transport.StartedAtUtc == null)
            _finishedSimulationMatches.Remove(room.OwnerPid);
        else if (transport != null)
        {
            DateTime now = DateTime.UtcNow;
            foreach (int hostPid in _finishedSimulationMatches
                         .Where(x => now - x.Value.FinishedAtUtc > SimulationResultWindow)
                         .Select(x => x.Key).ToArray())
                _finishedSimulationMatches.Remove(hostPid);
            _finishedSimulationMatches[room.OwnerPid] = new FinishedSimulationMatch
            {
                FinishedAtUtc = now, GoalScore = room.GoalScore,
                // The limit op155 gave the listen host, not the room's current setting.
                TimeLimitSeconds = transport.TimeLimitSeconds,
                Seconds = transport.StartedAtUtc is { } start ? Math.Max(0, (int)(now - start).TotalSeconds) : 0,
                JoinedAtSeconds = transport.JoinedAtSeconds,
                Unsettled = transport.Members.ToHashSet()
            };
        }
        BroadcastSimulationChannelLocked(room.Channel, new ChangeGameStateBC
            { RoomId = room.Id, GameState = room.State });
        BroadcastSimulationChannelLocked(room.Channel, BuildSimulationRoomBroadcast(room));
    }

    public static bool IsSimulationEndpointListening(int packedIp, int port,
        IEnumerable<System.Net.IPEndPoint> endpoints)
    {
        var address = new System.Net.IPAddress(BitConverter.GetBytes(packedIp));
        return endpoints.Any(x => x.Port == port && x.Address.Equals(address));
    }

    // InRoom: the player is in a Simulation room and goes back to the Simulation Center. When the
    // room's host aborts a running match, op115 will not follow, so the match ends for the whole
    // room and Aborted lists the members to send back.
    internal (bool InRoom, List<(Client Client, int Pid)> Aborted) AbortHostedMatch(int pid)
    {
        List<(Client Client, int Pid)> aborted = [];
        lock (_simulationLock)
        {
            if (!_simulationPidRoom.TryGetValue(pid, out byte roomId) || !_simulationRooms.TryGetValue(roomId, out var room))
                return (false, aborted);
            // op106 comes after the scoreboard; the guest's relay stays up until then.
            _p2pManager.StopRelay(pid);
            if (room.State != 0 && room.OwnerPid == pid)
            {
                foreach (int memberPid in room.Members.Keys)
                    if (Game.TryGetClient(memberPid, out var memberClient))
                    {
                        _p2pManager.StopRelay(memberClient);
                        aborted.Add((memberClient, memberPid));
                    }
                FinishSimulationMatchLocked(room);
                _logger.LogInformation("[Simulation] room {0} aborted by host PID {1}; returning {2} member(s).",
                    room.Id, pid, aborted.Count);
            }
        }
        return (true, aborted);
    }
}
