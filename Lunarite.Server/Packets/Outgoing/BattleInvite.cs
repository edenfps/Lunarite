using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class BattleInvite : IPacketSerialize
{
    public byte Axis;
    public byte Area;
    public byte[] Serialize() => [(byte)30, Axis, Area];
}
