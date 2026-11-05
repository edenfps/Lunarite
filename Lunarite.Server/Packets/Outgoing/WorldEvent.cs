using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class WorldEvent : IPacketSerialize
{
    public int Pid;
    public byte Event;
    public int Value;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)117); w.Write(Pid); w.Write(Event); w.Write(Value);
        return w.Buffer;
    }
}
