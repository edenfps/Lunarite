using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class KickOut : IPacketSerialize
{
    public byte[] Serialize() => [(byte)154];
}
