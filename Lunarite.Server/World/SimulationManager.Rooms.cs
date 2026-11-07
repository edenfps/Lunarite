using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Lunarite.Handlers;
using Lunarite.Network;
using Lunarite.Packets.Common;
using Lunarite.Packets.Outgoing;
using Lunarite.Storage;

namespace Lunarite.World;

// Simulation Center rooms: creating, joining, settings, sides and readiness, leaving.
public sealed partial class SimulationManager
{

    internal void ChangeRoomInfo(Client client, int map, byte gameType, ushort goalScore, byte goalTime, byte maxPlayers, byte limitClass)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(pid, out var room, out _) || room.OwnerPid != pid)
            {
                client.SendPacket(new ChangeRoomInfo { Result = SimulationAnswer.Error });
                return;
            }
            // The settings of a running match stay as it started (op155 already took them).
            if (room.State != 0)
            {
                client.SendPacket(new ChangeRoomInfo { Result = SimulationAnswer.AlreadyStart });
                return;
            }
            if (!IsSimulationGameType(gameType) || !IsSimulationGoal(gameType, goalScore, goalTime))
            {
                client.SendPacket(new ChangeRoomInfo { Result = SimulationAnswer.Error });
                return;
            }
            if (maxPlayers < room.Members.Count || !_battleZoneCatalog.SupportsRoom(map, maxPlayers)
                || limitClass > 3 || room.Members.Keys.Any(x => !IsSimulationClassEligible(x, limitClass)))
            {
                client.SendPacket(new ChangeRoomInfo
                    { Result = limitClass > 3 || room.Members.Keys.Any(x => !IsSimulationClassEligible(x, limitClass))
                        ? (byte)7 : (byte)2 });
                return;
            }
            room.Map = map; room.GameType = gameType; room.GoalScore = goalScore;
            room.GoalTime = goalTime; room.MaxPlayers = maxPlayers; room.LimitClass = limitClass;
            client.SendPacket(new ChangeRoomInfo());
            var changed = new ChangeRoomInfoBC
            {
                RoomId = room.Id, Map = map, GameType = gameType, GoalTime = goalTime,
                GoalScore = goalScore, MaxPlayers = maxPlayers, LimitClass = limitClass
            };
            BroadcastSimulationLocked(room, changed, pid);
            BroadcastSimulationChannelLocked(room.Channel, BuildSimulationRoomBroadcast(room));
        }
    }

    internal void ChangeSpecies(Client client, byte species)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(pid, out var room, out var member) || !IsSimulationSpecies(species))
            {
                client.SendPacket(new ChangeSpecies { Result = SimulationAnswer.Error });
                return;
            }
            // The reply carries the squad the server picked on the new side: the first with room
            // (MAX_SQUAD_PLAYER_NUM 4), else SC_ANSWER_FULL.
            byte? squad = FirstSimulationSquadWithRoom(room, species, pid);
            if (squad is not { } newSquad)
            {
                client.SendPacket(new ChangeSpecies { Result = SimulationAnswer.Full });
                return;
            }
            member.Species = species;
            member.Squad = newSquad;
            client.SendPacket(new ChangeSpecies { Squad = member.Squad });
            BroadcastSimulationLocked(room,
                new ChangeSpeciesBC { Pid = pid, Species = species, Squad = member.Squad }, pid);
        }
    }

    internal void ChangeSquad(Client client, byte squad)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(pid, out var room, out var member) || !IsSimulationSquad(squad))
            {
                client.SendPacket(new ChangeSquad { Result = SimulationAnswer.Error });
                return;
            }
            if (SimulationSquadCount(room, member.Species, squad, pid) >= SIMULATION_SQUAD_MAX)
            {
                client.SendPacket(new ChangeSquad { Result = SimulationAnswer.Full });
                return;
            }
            member.Squad = squad;
            client.SendPacket(new ChangeSquad());
            BroadcastSimulationLocked(room, new ChangeSquadBC { Pid = pid, Squad = squad }, pid);
        }
    }

    internal void CreateRoom(Client client, string? roomName, byte maxPlayers, int map, byte gameType, byte goalTime, ushort goalScore, byte isPublic, byte limitClass, string? password)
    {

        if (string.IsNullOrWhiteSpace(roomName) || !_battleZoneCatalog.SupportsRoom(map, maxPlayers)
            || !IsSimulationGameType(gameType) || !IsSimulationGoal(gameType, goalScore, goalTime))
        {
            _logger.LogWarning(
                "[SimulationCenter] rejected room create name='{0}' map={1} type={2} time={3} score={4} max={5}: invalid map/name/type/goal/player count.",
                roomName ?? "", map, gameType, goalTime, goalScore, maxPlayers);
            client.SendPacket(new CreateRoom { Result = SimulationAnswer.Error });
            return;
        }

        int pid = Game.PidOf(client);
        var character = _characterStore.Get(pid);
        if (character == null || !IsSimulationClassEligible(pid, limitClass))
        {
            _logger.LogWarning("[SimulationCenter] rejected room create PID {0} map={1} limitClass={2}: character/class gate.",
                pid, map, limitClass);
            client.SendPacket(new CreateRoom
                { Result = character == null ? (byte)1 : (byte)7 }); // SC_ANSWER_LEVEL_ERROR
            return;
        }
        byte roomId;
        lock (_simulationLock)
        {
            RemoveSimulationMemberLocked(pid, 0);
            roomId = 0;
            for (int attempt = 0; attempt < byte.MaxValue; attempt++)
            {
                _nextSimulationRoomId = _nextSimulationRoomId % byte.MaxValue + 1;
                byte candidate = (byte)_nextSimulationRoomId;
                if (!_simulationRooms.ContainsKey(candidate))
                {
                    roomId = candidate;
                    break;
                }
            }
            if (roomId == 0)
            {
                client.SendPacket(new CreateRoom { Result = SimulationAnswer.RoomFull });
                return;
            }
            var room = new SCRoomInfo
            {
                Id = roomId, OwnerPid = pid, Name = roomName, Password = password ?? "", Map = map,
                GameType = gameType, MaxPlayers = maxPlayers, GoalTime = goalTime,
                GoalScore = goalScore, IsPublic = isPublic, LimitClass = limitClass,
                Channel = _simulationPlayerChannels.GetValueOrDefault(pid)
            };
            byte species = CharacterStore.GetTribe(character.Index);
            room.Members[pid] = NewSimulationMember(client, pid, species);
            _simulationRooms[roomId] = room;
            _simulationPidRoom[pid] = roomId;
            // OnCreateRoom builds the local SCPlayerInfo from these fields, so the room must
            // already exist.
            client.SendPacket(new CreateRoom
                { RoomId = roomId, Species = species, FullConnection = 1 });
            BroadcastSimulationChannelLocked(room.Channel, BuildSimulationRoomBroadcast(room), pid);
        }
        _logger.LogInformation(
            "[SimulationCenter] Created room {0} '{1}' map={2} type={3} time={4} score={5} max={6} public={7} class={8} password={9}.",
            roomId, roomName, map, gameType, goalTime, goalScore, maxPlayers, isPublic, limitClass,
            string.IsNullOrEmpty(password) ? "no" : "yes");
    }

    internal void KickOut(Client client, int targetPid)
    {
        int ownerPid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(ownerPid, out var room, out _)
                || room.OwnerPid != ownerPid || targetPid == ownerPid
                || !room.Members.ContainsKey(targetPid))
                return;
            if (Game.TryGetClient(targetPid, out var target)) target.SendPacket(new KickOut());
            RemoveSimulationMemberLocked(targetPid, 1);
        }
    }

    internal bool QuickJoinRoom(Client client)
    {
        byte roomId;
        lock (_simulationLock)
        {
            int pid = Game.PidOf(client);
            var room = _simulationRooms.Values.FirstOrDefault(x => x.IsPublic != 0
                && x.Password.Length == 0 && x.State == 0 && x.Members.Count < x.MaxPlayers
                && IsSimulationClassEligible(pid, x.LimitClass));
            if (room == null)
            {
                client.SendPacket(new JoinRoom { Result = SimulationAnswer.NoRoom });
                return true;
            }
            roomId = room.Id;
        }
        return JoinRoom(client, roomId, "");
    }

    internal void ReadyCancel(Client client)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(pid, out var room, out var member)) return;
            member.Ready = false;
            member.State = (byte)(member.State & ~1);
            BroadcastSimulationLocked(room, new ReadyCancelBC { Pid = pid });
            BroadcastSimulationLocked(room, new ChangePlayerStateBC { Pid = pid, State = member.State });
        }
    }

    internal void Ready(Client client)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(pid, out var room, out var member))
            {
                client.SendPacket(new Ready { Result = SimulationAnswer.Error });
                return;
            }
            // The room menu lets the owner press Start (op128) before every guest is ready
            // (OnReadyButtonClick never checks IsAllGuestReady). Retail answers op143
            // SC_ANSWER_NO_READY and leaves the owner not ready, so Start stays pressable.
            if (room.OwnerPid == pid && room.Members.Values.Any(x => x.Pid != pid && !x.Ready))
            {
                client.SendPacket(new OwnerStart { Result = SimulationAnswer.NoReady });
                return;
            }
            member.Ready = true;
            member.State |= 1; // SC_PLAYER_STATE_READY
            BroadcastSimulationLocked(room, new Ready { Pid = pid });
            BroadcastSimulationLocked(room, new ChangePlayerStateBC { Pid = pid, State = member.State });
            _logger.LogInformation("[Simulation] room {0} PID {1} ready (owner={2}, all-guests-ready={3}).",
                room.Id, pid, room.OwnerPid == pid,
                room.Members.Values.All(x => x.Pid == room.OwnerPid || x.Ready));
            // The owner's Start button calls SendReady (op128), not SendStartMatch (op141).
            // With every guest ready, the transport handshake starts here.
            if (room.OwnerPid == pid && room.Members.Values.All(x => x.Pid == pid || x.Ready))
                BeginSimulationMatchLocked(client, pid, room);
        }
    }

    internal void SimulPlayerStateBusy(Client client, byte state)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!TryGetSimulationMemberLocked(pid, out var room, out var member)
                || !IsSimulationPlayerState(state)) return;
            member.State = state;
            BroadcastSimulationLocked(room, new SimulPlayerStateBusy { Pid = pid, State = state });
            BroadcastSimulationLocked(room, new ChangePlayerStateBC { Pid = pid, State = state });
        }
    }

    internal void LeaveRoom(int pid)
    {
        lock (_simulationLock) RemoveSimulationMemberLocked(pid, 0);
    }

    internal void JoinChannel(Client client, int pid, byte channel)
    {
        lock (_simulationLock)
        {
            _simulationChannelPids.Add(pid);
            _simulationPlayerChannels[pid] = channel;
            foreach (var room in _simulationRooms.Values.Where(x => x.Channel == channel))
                client.SendPacket(BuildSimulationRoomBroadcast(room));
        }
    }

    internal void LeaveChannel(Client client)
    {
        lock (_simulationLock) _simulationChannelPids.Remove(Game.PidOf(client));
    }

    internal void LeaveSimulationCenter(int pid)
    {
        lock (_simulationLock)
        {
            _simulationChannelPids.Remove(pid);
            _simulationPlayerChannels.Remove(pid);
            RemoveSimulationMemberLocked(pid, 0);
        }
    }

    private bool IsSimulationClassEligible(int pid, byte limitClass) =>
        limitClass <= 3 && CharacterManager.GetLevelClass(_playerStore.GetProgress(pid).Level) <= limitClass;

    internal bool JoinRoom(Client client, byte roomId, string password)
    {
        int pid = Game.PidOf(client);
        lock (_simulationLock)
        {
            if (!_simulationRooms.TryGetValue(roomId, out var room)
                || room.Channel != _simulationPlayerChannels.GetValueOrDefault(pid))
            {
                client.SendPacket(new JoinRoom { Result = SimulationAnswer.NoRoom });
                return true;
            }
            bool restoring = _simulationPidRoom.TryGetValue(pid, out byte currentRoomId)
                && currentRoomId == roomId && room.Members.ContainsKey(pid);
            if (!restoring && room.Members.Count >= room.MaxPlayers)
            {
                client.SendPacket(new JoinRoom { Result = SimulationAnswer.Full });
                return true;
            }
            if (room.State != 0)
            {
                client.SendPacket(new JoinRoom { Result = SimulationAnswer.AlreadyStart });
                return true;
            }
            if (!restoring && !string.Equals(room.Password, password, StringComparison.Ordinal))
            {
                client.SendPacket(new JoinRoom { Result = SimulationAnswer.WrongPassword });
                return true;
            }
            if (!IsSimulationClassEligible(pid, room.LimitClass))
            {
                client.SendPacket(new JoinRoom { Result = SimulationAnswer.LevelError });
                return true;
            }

            if (!restoring)
            {
                var character = _characterStore.Get(pid);
                byte species = CharacterStore.GetTribe(character?.Index ?? 0);
                // A joiner takes the first squad on its side with room (MAX_SQUAD_PLAYER_NUM 4),
                // as ChangeSpecies does.
                if (FirstSimulationSquadWithRoom(room, species, pid) is not { } squad)
                {
                    client.SendPacket(new JoinRoom { Result = SimulationAnswer.Full });
                    return true;
                }
                RemoveSimulationMemberLocked(pid, 0);
                var joined = NewSimulationMember(client, pid, species);
                joined.Squad = squad;
                room.Members[pid] = joined;
                _simulationPidRoom[pid] = roomId;
            }
            else
            {
                // After results, op106 returns the client to the Simulation Center and the UI
                // rejoins its room without the password; existing membership authorizes it.
                room.Members[pid].Ready = false;
                room.Members[pid].State = 0;
            }
            var response = new JoinRoom
            {
                ChoiceTribe = 0,
                Room = new SimulationRoomWire(room.Id, room.Name,
                    _characterStore.Get(room.OwnerPid)?.Name ?? "", room.Map, room.GameType,
                    room.MaxPlayers, room.GoalTime, room.GoalScore, room.IsPublic, 0, room.State, room.LimitClass, 0)
            };
            foreach (var m in room.Members.Values)
                response.Players.Add(BuildSimulationPlayerWire(m));
            client.SendPacket(response);
            if (!restoring)
            {
                var joinedPlayer = BuildSimulationPlayerWire(room.Members[pid]);
                BroadcastSimulationLocked(room, new JoinPlayerBCRoom
                    { RoomId = room.Id, Player = joinedPlayer }, pid);
                foreach (int channelPid in _simulationChannelPids)
                    if (!room.Members.ContainsKey(channelPid) && IsInSimulationChannelLocked(channelPid, room.Channel)
                        && Game.TryGetClient(channelPid, out var observer))
                        observer.SendPacket(new JoinPlayerBCChannel
                            { RoomId = room.Id, Player = joinedPlayer });
                BroadcastSimulationChannelLocked(room.Channel, BuildSimulationRoomBroadcast(room));
            }
            else
            {
                _logger.LogInformation("[Simulation] restored PID {0} to room {1} after match results.", pid, room.Id);
            }
        }
        return true;
    }

    private SimulationPlayerWire BuildSimulationPlayerWire(SCPlayerInfo member)
    {
        var character = _characterStore.Get(member.Pid);
        var progress = _playerStore.GetProgress(member.Pid);
        return new SimulationPlayerWire(member.Pid, character?.Name ?? "",
            (byte)Math.Clamp(progress.Level, 0, byte.MaxValue), character?.Style ?? 0, progress.Rank,
            member.Squad, member.Species, member.State, 1, 0,
            member.Address, member.Port, member.Address, member.Port,
            _clanStore.ForPid(member.Pid)?.Mark ?? 0);
    }

    private SCPlayerInfo NewSimulationMember(Client client, int pid, byte species)
    {
        int address = unchecked((int)P2PManager.PackedIp(client.IPAddress.MapToIPv4().ToString()));
        // FP2PSocket ignores MULTIHOME, binds INADDR_ANY and counts up from 7777 for each extra
        // client on one machine. UE3 sockets honor MULTIHOME, so the address stays per client.
        ushort port = EnsureSimulationClientPortLocked(client.Id);
        return new SCPlayerInfo { Pid = pid, Species = species, Address = address, Port = port };
    }

    private ushort EnsureSimulationClientPortLocked(int clientId)
    {
        const ushort defaultP2PPort = 7777;
        ushort existing = _simulationClientPorts.GetValueOrDefault(clientId);
        if (existing != 0) return existing;
        var used = _simulationClientPorts.Values.ToHashSet();
        ushort port = defaultP2PPort;
        while (used.Contains(port) && port < ushort.MaxValue) port++;
        _simulationClientPorts[clientId] = port;
        return port;
    }

    private byte? FirstSimulationSquadWithRoom(SCRoomInfo room, byte species, int excludePid)
    {
        for (byte squad = 0; squad < 5; squad++)
            if (SimulationSquadCount(room, species, squad, excludePid) < SIMULATION_SQUAD_MAX) return squad;
        return null;
    }

    private static int SimulationSquadCount(SCRoomInfo room, byte species, byte squad, int excludePid) =>
        room.Members.Values.Count(x => x.Pid != excludePid && x.Squad == squad
            && (x.Species is 1 or 2) == (species is 1 or 2));

    private bool TryGetSimulationMemberLocked(int pid, out SCRoomInfo room, out SCPlayerInfo member)
    {
        room = null!; member = null!;
        return _simulationPidRoom.TryGetValue(pid, out byte roomId)
            && _simulationRooms.TryGetValue(roomId, out room!)
            && room.Members.TryGetValue(pid, out member!);
    }

    private static void BroadcastSimulationLocked(SCRoomInfo room, IPacketSerialize packet, int exceptPid = 0)
    {
        foreach (int memberPid in room.Members.Keys)
            if (memberPid != exceptPid && Game.TryGetClient(memberPid, out var target))
                target.SendPacket(packet);
    }

    private CreateRoomBC BuildSimulationRoomBroadcast(SCRoomInfo room)
    {
        var owner = _characterStore.Get(room.OwnerPid);
        byte ownerLevel = (byte)Math.Clamp(_playerStore.GetProgress(room.OwnerPid).Level, 0, 255);
        byte average = room.Members.Count == 0 ? (byte)0 : (byte)Math.Clamp(
            (int)room.Members.Keys.Average(x => _playerStore.GetProgress(x).Level), 0, 255);
        return new CreateRoomBC
        {
            Room = new SimulationRoomWire(room.Id, room.Name, owner?.Name ?? "", room.Map,
                room.GameType, room.MaxPlayers, room.GoalTime, room.GoalScore, room.IsPublic,
                0, room.State, room.LimitClass, 0),
            CurrentPlayers = (byte)room.Members.Count,
            SapiPlayers = (byte)room.Members.Values.Count(x => x.Species is 1 or 2),
            AlterPlayers = (byte)room.Members.Values.Count(x => x.Species is 3 or 4),
            OwnerLevel = ownerLevel,
            AverageLevel = average
        };
    }

    private bool IsInSimulationChannelLocked(int pid, byte channel) =>
        _simulationChannelPids.Contains(pid) && _simulationPlayerChannels.GetValueOrDefault(pid) == channel;

    private void BroadcastSimulationChannelLocked(byte channel, IPacketSerialize packet, int exceptPid = 0)
    {
        foreach (int channelPid in _simulationChannelPids)
            if (channelPid != exceptPid && IsInSimulationChannelLocked(channelPid, channel)
                && Game.TryGetClient(channelPid, out var target))
                target.SendPacket(packet);
    }

    private void RemoveSimulationMemberLocked(int pid, byte reason)
    {
        bool abandonedMatch = _simulationTransports.Remove(pid, out var lostMatch);
        foreach (var transport in _simulationTransports.Values) transport.Pending.Remove(pid);
        if (!_simulationPidRoom.Remove(pid, out byte roomId) || !_simulationRooms.TryGetValue(roomId, out var room)) return;
        room.Members.Remove(pid);
        _p2pManager.StopRelay(pid);
        // A running match whose host left continues on another member (HuxleyBattleGame restores
        // scores, clock and player records from the backup the old host sent).
        bool rehost = abandonedMatch && lostMatch!.Started && room.Members.Count != 0;
        if (abandonedMatch && !rehost)
        {
            room.State = 0;
            foreach (var member in room.Members.Values)
            {
                member.Ready = false;
                member.State = 0;
                _p2pManager.StopRelay(member.Pid);
            }
            BroadcastSimulationChannelLocked(room.Channel, new ChangeGameStateBC
                { RoomId = room.Id, GameState = room.State });
        }
        BroadcastSimulationLocked(room, new LeavePlayerBCRoom { Pid = pid, RoomId = room.Id });
        BroadcastSimulationChannelLocked(room.Channel, new LeavePlayerBCChannel { RoomId = room.Id });
        if (room.Members.Count == 0)
        {
            _simulationRooms.Remove(roomId);
            BroadcastSimulationChannelLocked(room.Channel, new DestroyRoomBC { RoomId = roomId });
        }
        else
        {
            if (room.OwnerPid == pid)
            {
                room.OwnerPid = room.Members.Keys.First();
                var newOwner = room.Members[room.OwnerPid];
                var character = _characterStore.Get(room.OwnerPid);
                BroadcastSimulationLocked(room, new NewOwnerBC
                {
                    RoomId = room.Id,
                    Pid = room.OwnerPid,
                    Name = character?.Name ?? "",
                    Level = (byte)Math.Clamp(_playerStore.GetProgress(room.OwnerPid).Level, 0, 255),
                    PublicAddress = newOwner.Address, PublicPort = newOwner.Port,
                    PrivateAddress = newOwner.Address, PrivatePort = newOwner.Port
                });
            }
            BroadcastSimulationChannelLocked(room.Channel, BuildSimulationRoomBroadcast(room));
            if (rehost && Game.TryGetClient(room.OwnerPid, out var newOwnerClient))
            {
                _logger.LogInformation("[Simulation] room {0} host PID {1} left a running match; PID {2} hosts it again ({3}:{4}, {5} s left).",
                    room.Id, pid, room.OwnerPid, lostMatch!.TeamScore0, lostMatch.TeamScore1, lostMatch.RemainingTime);
                BeginSimulationMatchLocked(newOwnerClient, room.OwnerPid, room, lostMatch);
            }
        }
    }
}
