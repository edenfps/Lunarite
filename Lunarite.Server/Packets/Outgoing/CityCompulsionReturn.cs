using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class CityCompulsionReturn : IPacketSerialize
{
    public byte[] Serialize() => [(byte)28];
}
