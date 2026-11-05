using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ChangeSquad : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize() { using var w = new PacketWriter(); w.Write((byte)152); w.Write(Result); return w.Buffer; }
}
