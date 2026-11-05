using Lunarite.Network;
namespace Lunarite.Packets.Outgoing;

public sealed class BuyItem : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize() { using var w = new PacketWriter(); w.Write((byte)64); w.Write(Result); return w.Buffer; }
}
