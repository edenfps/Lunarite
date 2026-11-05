using System.Collections.Generic;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class JoinRoom : IPacketSerialize
{
    public byte Result;
    public byte ChoiceTribe;
    public SimulationRoomWire Room;
    public List<SimulationPlayerWire> Players { get; } = [];

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)132); w.Write(Result); w.Write((byte)Players.Count); w.Write(ChoiceTribe);
        if (Result != 0) return w.Buffer;
        // Native ProcJoinSimulRoom: bssdbbbwbbbb.
        w.Write(Room.Id); w.Write(Room.Name); w.Write(Room.Owner); w.Write(Room.Map);
        w.Write(Room.GameType); w.Write(Room.MaxPlayers); w.Write(Room.GoalTime);
        w.Write(Room.GoalScore); w.Write(Room.IsPublic); w.Write(Room.RemainTime);
        w.Write(Room.GameState); w.Write(Room.LimitClass);
        // Native SCPlayerInfo body plus the four P2P address fields consumed between
        // spectator and clan mark: dsbbbbbbbbdwdwbd.
        foreach (var player in Players)
        {
            w.Write(player.Pid); w.Write(player.Name); w.Write(player.Level); w.Write(player.Style);
            w.Write(player.Rank);
            w.Write(player.Squad); w.Write(player.Species); w.Write(player.State); w.Write(player.FullConnection);
            w.Write(player.Spectator); w.Write(player.PublicAddress); w.Write(player.PublicPort);
            w.Write(player.PrivateAddress); w.Write(player.PrivatePort);
            w.Write((byte)0); w.Write(player.ClanMark);
        }
        return w.Buffer;
    }
}
