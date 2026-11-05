using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ChangeSquadBC : IPacketSerialize
{
    public int Pid;
    public byte Squad;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)153); w.Write(Pid); w.Write(Squad);
        return w.Buffer;
    }
}
