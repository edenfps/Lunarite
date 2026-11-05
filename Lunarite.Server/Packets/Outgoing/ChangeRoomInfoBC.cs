using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ChangeRoomInfoBC : IPacketSerialize
{
    public byte RoomId;
    public int Map;
    public byte GameType;
    public byte GoalTime;
    public ushort GoalScore;
    public byte MaxPlayers;
    public byte LimitClass;
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)139); w.Write(RoomId); w.Write(Map);
        w.Write(GameType); w.Write(GoalTime); w.Write(GoalScore); w.Write(MaxPlayers); w.Write(LimitClass);
        return w.Buffer;
    }
}
