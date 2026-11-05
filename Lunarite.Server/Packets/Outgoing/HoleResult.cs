using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class HoleResult : IPacketSerialize
{
    public byte Result;
    public byte Playable;
    public byte[] Serialize() => [(byte)125, Result, Playable];
}
