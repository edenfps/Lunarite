using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ActiveEvent : IPacketSerialize
{
    public int Event;
    public byte Active;
    public int Parameter;
    public int Value1;
    public int Value2;

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)4); w.Write(Event); w.Write(Active); w.Write(Parameter); w.Write(Value1); w.Write(Value2);
        return w.Buffer;
    }
}
