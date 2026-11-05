using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class RemoveItemBind : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize() { using var w = new PacketWriter(); w.Write((byte)229); w.Write(Result); return w.Buffer; }
}
