using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ReadyCancelBC : IPacketSerialize
{
    public int Pid;
    public byte[] Serialize() { using var w = new PacketWriter(); w.Write((byte)141); w.Write(Pid); return w.Buffer; }
}
