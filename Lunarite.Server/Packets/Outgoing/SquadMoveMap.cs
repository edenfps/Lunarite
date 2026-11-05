using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class SquadMoveMap : IPacketSerialize
{
    public int Pid;
    public byte MapIndex;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)109);
        writer.Write(Pid);
        writer.Write(MapIndex);
        return writer.Buffer;
    }
}
