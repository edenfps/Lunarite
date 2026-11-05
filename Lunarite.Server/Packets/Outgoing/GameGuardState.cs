using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class GameGuardState : IPacketSerialize
{
    public byte State;
    public byte[] Serialize() => [(byte)235, State];
}
