using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ChangeSpecies : IPacketSerialize
{
    public byte Result;
    public byte Squad;
    public byte[] Serialize()
    {
        using var w = new PacketWriter(); w.Write((byte)134); w.Write(Result);
        if (Result != 1) w.Write(Squad);
        return w.Buffer;
    }
}
