using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class CreateRoomBC : IPacketSerialize
{
    public SimulationRoomWire Room;
    public byte CurrentPlayers;
    public byte SapiPlayers;
    public byte AlterPlayers;
    public byte OwnerLevel;
    public byte AverageLevel;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)145); w.Write(Room.Id); w.Write(Room.Name); w.Write(Room.Owner); w.Write(Room.Map);
        // ProcMakeRoomInfo: bssdbbbbbbwbbbbbb. Unlike ProcJoinSimulRoom, this
        // channel-list shape puts timing/state/capacity before the score word.
        w.Write(Room.GameType); w.Write(Room.GoalTime); w.Write(Room.RemainTime);
        w.Write(Room.GameState); w.Write(Room.MaxPlayers); w.Write(CurrentPlayers);
        w.Write(Room.GoalScore); w.Write(Room.IsPublic); w.Write(OwnerLevel);
        w.Write(SapiPlayers); w.Write(AlterPlayers); w.Write(Room.LimitClass); w.Write(AverageLevel);
        return w.Buffer;
    }
}
