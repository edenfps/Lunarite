using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class Ready : IPacketSerialize
{
    public byte Result;
    public int Pid;
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)140); w.Write(Result);
        if (Result == 0) w.Write(Pid);
        return w.Buffer;
    }
}
