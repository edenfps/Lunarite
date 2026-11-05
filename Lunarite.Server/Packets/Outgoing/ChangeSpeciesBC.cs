using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ChangeSpeciesBC : IPacketSerialize
{
    public int Pid;
    public byte Species;
    public byte Squad;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)138); w.Write(Species); w.Write(Pid); w.Write(Squad);
        return w.Buffer;
    }
}
