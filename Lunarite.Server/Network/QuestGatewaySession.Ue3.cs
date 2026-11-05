using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

using Lunarite.Network.Ue3;

using Microsoft.Extensions.Logging;

namespace Lunarite.Network;

internal sealed partial class QuestGatewaySession
{
    private void OnReceive(IAsyncResult ar)
    {
        var state = ar.AsyncState as RecvState;
        var socket = state?.Sock ?? _udp;
        if (socket == null) return;
        int received;
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        // Throws ObjectDisposedException for an evicted socket.
        try { received = socket.EndReceiveFrom(ar, ref from); }
        catch (Exception) { return; }

        var buffer = state?.Buf ?? Array.Empty<byte>();
        var data = new byte[received];
        Array.Copy(buffer, data, received);

        if (_proxyMode)
        {
            var fromEp = (IPEndPoint)from;
            bool fromServer = fromEp.Port == _proxyServer.Port;
            if (fromServer)
            {
                if (_proxyClient != null) _udp!.SendTo(data, _proxyClient);
                _logger.LogWarning("[Proxy] S->C {0}b: {1}", received, Convert.ToHexString(data));
            }
            else
            {
                _proxyClient = from;
                _udp!.SendTo(data, _proxyServer);
                _logger.LogWarning("[Proxy] C->S {0}b: {1}", received, Convert.ToHexString(data));
            }
            if (IsLive(socket)) BeginReceiveOn(socket);
            return;
        }

        if (_captureMode)
        {
            byte t = received > 0 ? data[received - 1] : (byte)0;
            _logger.LogInformation("[Gateway] CAPTURE {0} bytes from {1} (lastByte={2}): {3}",
                received, from, t, Convert.ToHexString(data));
            if (IsLive(socket)) BeginReceiveOn(socket);
            return;
        }

        // Plain UDP: the whole datagram is the UE3 packet, with no P2P type byte.
        if (_plainTransport)
        {
            lock (_lock) _connected = true;
            HandleUe3Packet(new ReadOnlySpan<byte>(data, 0, received), socket);
            if (IsLive(socket)) BeginReceiveOn(socket);
            return;
        }

        byte type = received > 0 ? data[received - 1] : (byte)0xFF;

        if (type == 4 && received == 12)
        {
            _logger.LogInformation("[Gateway] PING ECHO (Ack={0}) — transport CONFIRMED.", data[0]);
        }
        else if (type == 2)
        {
            // A bare reliable-close bunch is also type 2, so _connected is set on CHALLENGE instead.
            HandleUe3Packet(new ReadOnlySpan<byte>(data, 0, received - 1), socket);
        }
        else
        {
            _logger.LogWarning("[Gateway] RECV {0} bytes (type={1}): {2}", received, type, Convert.ToHexString(data));
        }

        if (IsLive(socket)) BeginReceiveOn(socket);
    }

    // PacketId, then acks and bunches. recvSock identifies the pool port a CHALLENGE arrived on.
    private void HandleUe3Packet(ReadOnlySpan<byte> payload, Socket recvSock)
    {
        var reader = BitReader.FromPacket(payload);
        if (reader == null) return; // all-zero, no stop bit

        int packetId = (int)reader.ReadInt(MaxPacketId);
        var summary = new List<string>();
        int bunches = 0, acks = 0;

        while (!reader.AtEnd && !reader.Overflowed)
        {
            bool isAck = reader.ReadBit();
            if (isAck)
            {
                uint ackId = reader.ReadInt(MaxPacketId);
                acks++;
                summary.Add($"ACK {ackId}");
            }
            else
            {
                // Bunch header (UE3 3240): bControl gates bOpen/bClose; ChType if (bReliable||bOpen).
                bool bControl = reader.ReadBit();
                bool bOpen = bControl && reader.ReadBit();
                bool bClose = bControl && reader.ReadBit();
                // This build has no bDormant bit after bClose.
                bool bReliable = reader.ReadBit();
                uint chIndex = reader.ReadInt(MaxChannels);
                uint chSeq = bReliable ? reader.ReadInt(MaxChSequence) : 0;
                uint chType = (bReliable || bOpen) ? reader.ReadInt(ChTypeMax) : 0;
                uint bunchBits = reader.ReadInt(MaxBunchBits);
                bunches++;

                // A control bunch can carry several FString commands.
                int payloadStart = reader.Position;
                int payloadEnd = payloadStart + (int)bunchBits;
                var cmds = new List<string>();
                if (chIndex == 0)
                {
                    while (reader.Position + 32 <= payloadEnd && !reader.Overflowed)
                    {
                        int length = reader.ReadInt32();
                        if (length <= 0 || length > 512 || reader.Position + length * 8 > payloadEnd) break;
                        var sb = new System.Text.StringBuilder();
                        for (int i = 0; i < length && !reader.Overflowed; i++) sb.Append((char)reader.ReadByte());
                        cmds.Add(sb.ToString().TrimEnd('\0'));
                    }
                }
                else
                {
                    // Actor channel. The host replicates the gateway actor here and calls
                    // MasterServerEventFired(Evt,Orig,Instig) as a reliable RPC. An open bunch
                    // starts with an actor header that is not parsed; it is kept in the payload.
                    byte[] bunchPayload = reader.CopyBits(payloadStart, (int)bunchBits);
                    HandleActorBunch(chIndex, chType, bOpen, bClose, bReliable, chSeq, (int)bunchBits, bunchPayload);
                }
                while (reader.Position < payloadEnd && !reader.Overflowed) reader.ReadBit();

                summary.Add($"BUNCH ch={chIndex} type={chType} open={(bOpen ? 1 : 0)} close={(bClose ? 1 : 0)} rel={(bReliable ? 1 : 0)} seq={chSeq} bits={bunchBits} cmds=[{string.Join(" | ", cmds)}]");

                foreach (var cmd in cmds)
                    HandleServerCommand(cmd, recvSock);
            }
        }

        int count = System.Threading.Interlocked.Increment(ref _ue3RecvCount);
        // Log bunches, parse overflows, the first 30 packets and every 100th.
        if (bunches > 0 || reader.Overflowed || count <= 30 || count % 100 == 0)
            _logger.LogWarning("[Gateway] RECV#{0} UE3 pkt#{1} [{2}]{3}  raw={4}",
                count, packetId, summary.Count > 0 ? string.Join(", ", summary) : "empty",
                reader.Overflowed ? " (OVERFLOW - format off)" : "", Convert.ToHexString(payload));

        // Acks are piggybacked on the next outgoing packet or flushed by StartAckFlush.
        lock (_lock) { if (packetId > _inPacketId) _inPacketId = packetId; _pendingAcks.Add(packetId); }
    }

    // Resolves MasterServerEventFired's SequenceEvent through the package map; other actor
    // replication is logged verbatim.
    private void HandleActorBunch(uint chIndex, uint chType, bool bOpen, bool bClose,
        bool bReliable, uint chSeq, int bunchBits, byte[] payload)
    {
        int bunchNumber = System.Threading.Interlocked.Increment(ref _actorBunchCount);

        lock (_lock)
        {
            if (bunchBits > _chMaxBits.GetValueOrDefault(chIndex)) _chMaxBits[chIndex] = bunchBits;
        }
        if (bClose && !bOpen && chIndex != 2)
        {
            int maxb;
            lock (_lock) maxb = _chMaxBits.GetValueOrDefault(chIndex);
            if (maxb >= _enemyBitsThreshold)
            {
                System.Threading.Interlocked.Increment(ref _killsThisQuest);
                _logger.LogInformation("[Gateway] ENEMY DEATH telemetry (ch={0} maxbits={1}); awaiting exact op56 identity for loot.",
                    chIndex, maxb);
                lock (_lock) _chMaxBits.Remove(chIndex);  // the index is reused for the next actor
            }
        }
        string hex = Convert.ToHexString(payload);
        string ts = DateTime.Now.ToString("HH:mm:ss.fff");

        // After the open bunch, each ch=2 bunch starts with a FieldNetIndex. 45/46 are inherited
        // sound RPCs; 132 is MasterServerEventFired(Evt,Originator,Instigator).
        if (chIndex == 2 && !bOpen && bunchBits >= 7)
        {
            var fieldReader = new BitReader(payload, bunchBits);
            uint field = fieldReader.ReadIntValueAware(GatewayNetFieldCount);
            string known = field switch
            {
                20 => "Actor/PlayerController field 20",
                45 => "Kismet_ClientPlaySound",
                46 => "Kismet_ClientStopSound",
                131 => "bRemoteGateway",
                132 => "MasterServerEventFired",
                133 => "FinishRallyPoint",
                134 => "ServerSetObjectiveComplete",
                _ => "unknown/inherited"
            };
            if (field == MasterServerEventFiredField)
            {
                int eventNo = System.Threading.Interlocked.Increment(ref _masterEventCount);
                Ue3PackageMapCatalog.ObjectRef? eventRef = DecodeMasterEventObject(fieldReader);
                _logger.LogInformation("[Gateway] >>> MASTER EVENT RPC #{0}: field={1}, remainingBits={2}, raw={3}",
                    eventNo, field, bunchBits - fieldReader.Position, hex);
                if (eventRef is not null)
                    _logger.LogInformation("[Gateway] >>> MASTER EVENT OBJECT: {0} ({1}, package={2}, export={3})",
                        eventRef.Path, eventRef.ClassName, eventRef.Package, eventRef.ExportIndex);
                try
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "master_events.log"),
                        $"[{ts}] #{eventNo} seq={chSeq} bits={bunchBits} event={eventRef?.Path ?? "unresolved"} " +
                        $"argsBits={bunchBits - fieldReader.Position} payload={hex}\n");
                }
                catch { }
            }
            else if (field == FinishRallyPointField)
            {
                string? transactionName = DecodeNameParameter(fieldReader);
                _logger.LogInformation("[Gateway] >>> FINISH RALLY RPC: transaction={0}, raw={1}",
                    transactionName ?? "unresolved", hex);
            }
            else if (field != 45 && field != 46)
            {
                _logger.LogInformation("[Gateway] ch2 field={0} ({1}), bits={2}.", field, known, bunchBits);
            }
        }

        _logger.LogInformation("[Gateway] *** ACTOR BUNCH #{0} ch={1} type={2} open={3} close={4} rel={5} seq={6} bits={7} payload={8}",
            bunchNumber, chIndex, chType, bOpen ? 1 : 0, bClose ? 1 : 0, bReliable ? 1 : 0, chSeq, bunchBits, hex);
        try
        {
            System.IO.File.AppendAllText(_actorCapPath,
                $"[{ts}] #{bunchNumber} ch={chIndex} type={chType} open={(bOpen ? 1 : 0)} close={(bClose ? 1 : 0)} rel={(bReliable ? 1 : 0)} seq={chSeq} bits={bunchBits} payload={hex}\n");
            // ch=2 is HuxleyQuestMasterServerGateway_0.
            if (chIndex == 2)
                System.IO.File.AppendAllText(_ch2RpcPath,
                    $"[{ts}] seq={chSeq} bits={bunchBits} rel={(bReliable ? 1 : 0)} open={(bOpen ? 1 : 0)} payload={hex}\n");
        }
        catch { /* best-effort capture */ }
    }

    // RPC parameters have a presence bit. UObject serialization then has a dynamic-actor bit;
    // SequenceEvent is static, followed by SerializeInt(index, PackageMap.MaxObjectIndex).
    private Ue3PackageMapCatalog.ObjectRef? DecodeMasterEventObject(BitReader reader)
    {
        if (_packageMap.MaxObjectIndex <= 0 || reader.NumBits - reader.Position < 2) return null;
        if (!reader.ReadBit()) return null; // absent Evt
        if (reader.ReadBit()) return null;  // dynamic actor, invalid for SequenceEvent
        uint index = reader.ReadInt((uint)_packageMap.MaxObjectIndex);
        return reader.Overflowed ? null : _packageMap.Resolve((int)index);
    }

    private string? DecodeNameParameter(BitReader reader)
    {
        if (_packageMap.MaxNameIndex <= 0 || reader.AtEnd || !reader.ReadBit()) return null;
        uint index = reader.ReadInt((uint)_packageMap.MaxNameIndex + 1);
        if (reader.Overflowed || index >= _packageMap.MaxNameIndex) return null;
        return _packageMap.ResolveName((int)index);
    }

    // Dispatch a single text command received from the server on the control channel.
    private void HandleServerCommand(string cmd, Socket recvSock)
    {
        if (cmd.StartsWith("CHALLENGE ") && cmd.Contains("CHALLENGE="))
        {
            // Permission passed on this port. Ack flushing keeps the server's reliable window
            // moving through CHALLENGE -> WELCOME -> JOIN.
            FreezeOnto(recvSock);
            StartAckFlush();
            int challenge = ParseIntOption(cmd, "CHALLENGE=");
            System.Threading.Tasks.Task.Run(() => SendLoginText(challenge));
        }
        else if (cmd.StartsWith("USES "))
        {
            // Static UObject RPC arguments index into this ordered package list, capped by the
            // negotiated generation, so record it before answering HAVE.
            string guid = ParseStrOption(cmd, "GUID=");
            int gen = ParseIntOption(cmd, "GEN=");
            string package = ParseStrOption(cmd, "PKG=");
            string fileName = ParseStrOption(cmd, "FNAME=");
            if (!_packageMap.AddUse(new(package, fileName, guid, gen), out string mapError))
                _logger.LogWarning("[Gateway] package-map metadata unavailable: {0}", mapError);
            if (guid.Length > 0)
                SendControlText($"HAVE GUID={guid} GEN={gen}", "HAVE");
        }
        else if (cmd.StartsWith("WELCOME"))
        {
            // JOIN spawns our player (MasterServerGateway when the URL has ?MasterServer).
            _logger.LogInformation("[Gateway] >>> {0}  -> sending JOIN", cmd);
            SendControlText("JOIN", "JOIN");
        }
        else if (cmd.StartsWith("SERVERREADY"))
        {
            // Level load finished. A real-PID HELLO now reaches TheWorld while the one-time
            // permission is still valid.
            Action<int, int>? roomReady = null;
            int ownerPid = 0, roomPid = 0;
            lock (_lock)
            {
                if (!_serverReady)
                {
                    _serverReady = true;
                    roomReady = _roomReady;
                    ownerPid = _ownerPid;
                    roomPid = _roomPid;
                    _logger.LogInformation("[Gateway] SERVERREADY — level loaded; firing real HELLO pool now.");
                }
            }
            roomReady?.Invoke(ownerPid, roomPid);
        }
        else if (cmd.StartsWith("FAILURE") || cmd.StartsWith("FAILCODE"))
        {
            _logger.LogWarning("[Gateway] server rejected us: {0}", cmd);
        }
        else if (cmd.Trim().Length > 0)
        {
            // Other control-channel text is logged to control_text.log.
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            _logger.LogWarning("[Gateway] *** UNHANDLED CONTROL TEXT: {0}", cmd);
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "control_text.log"), $"[{ts}] {cmd}\n");
            }
            catch { }
        }
    }

    // Starts a packet with PacketId and all pending acks; bunches follow.
    private BitWriter NewPacketWithAcks(out int outId)
    {
        var packet = new BitWriter();
        lock (_lock)
        {
            outId = _outPacketId++;
            packet.WriteInt((uint)outId, MaxPacketId);
            foreach (var ackId in _pendingAcks)
            {
                packet.WriteBit(true);               // IsAck
                packet.WriteInt((uint)ackId, MaxPacketId);
            }
            _pendingAcks.Clear();
        }
        return packet;
    }

    private void StartAckFlush()
    {
        lock (_lock) { if (_ackFlushStarted) return; _ackFlushStarted = true; }
        System.Threading.Tasks.Task.Run(async () =>
        {
            while (true)
            {
                await System.Threading.Tasks.Task.Delay(30);
                bool any;
                lock (_lock) any = _pendingAcks.Count > 0;
                if (any)
                {
                    var packet = NewPacketWithAcks(out _);
                    SendPacket(packet, "acks", verbose: false);
                }
            }
        });
    }

    private void SendPacket(BitWriter packet, string label, bool verbose = true)
    {
        packet.Terminate();
        var ue3 = packet.ToArray();
        byte[] datagram;
        if (_plainTransport)
        {
            datagram = ue3;
        }
        else
        {
            datagram = new byte[ue3.Length + 1];
            Array.Copy(ue3, datagram, ue3.Length);
            datagram[^1] = 0x02;                     // P2P transport type 2 = direct game data
        }
        try
        {
            IPEndPoint target;
            lock (_lock) target = new IPEndPoint(_target.Address, _target.Port);
            _udp!.SendTo(datagram, target);
            if (verbose)
                _logger.LogInformation("[Gateway] SENT {0} ({1} bytes): {2}", label, datagram.Length, Convert.ToHexString(datagram));
        }
        catch (Exception ex) { _logger.LogWarning("[Gateway] send failed ({0}): {1}", label, ex.Message); }
    }
}
