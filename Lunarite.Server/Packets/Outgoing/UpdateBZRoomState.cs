using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op144 ProcBattleZoneRoomState parses `bd`. The client converts
// the timeout from milliseconds to seconds before OnUpdateBZRoomState.
public sealed class UpdateBZRoomState : IPacketSerialize
{
    public byte State;
    public int TimeoutMilliseconds;

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)144); w.Write(State); w.Write(TimeoutMilliseconds);
        return w.Buffer;
    }
}
