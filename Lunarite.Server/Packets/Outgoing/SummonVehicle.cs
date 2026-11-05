using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class SummonVehicle : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize() => [(byte)174, Result];
}
