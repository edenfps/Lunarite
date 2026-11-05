using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ChangePlayerStateBC : IPacketSerialize
{
    public int Pid;
    public byte State;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)150); w.Write(Pid); w.Write(State);
        return w.Buffer;
    }
}
