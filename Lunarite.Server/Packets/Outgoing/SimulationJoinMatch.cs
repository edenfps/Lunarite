using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class SimulationJoinMatch : IPacketSerialize
{
    public int HostIp;
    public byte[] Key = new byte[16];
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)156); w.Write(HostIp); w.Write(Key);
        return w.Buffer;
    }
}
