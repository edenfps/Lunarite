using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class NATTypeResult : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize() => [(byte)123, Result];
}
