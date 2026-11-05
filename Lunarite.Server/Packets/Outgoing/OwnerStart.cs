using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class OwnerStart : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize() { using var w = new PacketWriter(); w.Write((byte)143); w.Write(Result); return w.Buffer; }
}
