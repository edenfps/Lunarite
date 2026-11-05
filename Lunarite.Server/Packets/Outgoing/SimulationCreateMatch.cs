using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native op155: create and host a Simulation Center listen match.
public sealed class SimulationCreateMatch : IPacketSerialize
{
    public string Map = "";
    public byte[] Key = new byte[16];
    public byte MinPlayers = 1;
    public byte GameType;
    public ushort TimeLimit;
    public ushort GoalScore;
    public byte SecretRoom;
    public ushort TeamScore0, TeamScore1, RemainingTime;
    public SimulationPlayerState Player = new();
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)155); w.Write(Map); w.Write(Key);
        w.Write(MinPlayers); w.Write(GameType); w.Write(TimeLimit); w.Write(GoalScore); w.Write(SecretRoom);
        w.Write(TeamScore0); w.Write(TeamScore1); w.Write(RemainingTime);
        Player.WritePermissionBody(w, false); return w.Buffer;
    }
}
