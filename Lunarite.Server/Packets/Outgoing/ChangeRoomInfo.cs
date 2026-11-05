using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class ChangeRoomInfo : IPacketSerialize
{
    public byte Result;
    public byte[] Serialize() => [(byte)135, Result];
}
