using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// OnSimulRoomMsg(int messageType, int secondsLeft). Native op225 reads byte+int
// ("bd"); types 0..3 are ALLREADY, LEFTSECOND, KICKOWNER, CANCEL_AUTOSTART.
public sealed class SimulRoomMsg : IPacketSerialize
{
    public byte MessageType;
    public int SecondsLeft;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)225); w.Write(MessageType); w.Write(SecondsLeft);
        return w.Buffer;
    }
}
