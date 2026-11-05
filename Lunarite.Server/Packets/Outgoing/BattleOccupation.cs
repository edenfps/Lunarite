using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class BattleOccupation : IPacketSerialize
{
    public byte Server;
    public byte FromSpecies;
    public byte ToSpecies;
    public byte[] Serialize() => [(byte)34, Server, FromSpecies, ToSpecies];
}
