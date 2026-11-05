using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class SimulPlayerStateBusy : IPacketSerialize
{
    public int Pid;
    public byte State;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)142); w.Write(Pid); w.Write(State);
        return w.Buffer;
    }
}
