using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Agent/channel membership event. Native op147 prefixes the room ID and then
// consumes the full SCPlayerInfo body including its four P2P endpoint fields.
public sealed class JoinPlayerBCChannel : IPacketSerialize
{
    public byte RoomId;
    public SimulationPlayerWire Player;
    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        w.Write((byte)147); w.Write(RoomId);
        var player = Player;
        w.Write(player.Pid); w.Write(player.Name); w.Write(player.Level); w.Write(player.Style);
        w.Write(player.Rank);
        w.Write(player.Squad); w.Write(player.Species); w.Write(player.State); w.Write(player.FullConnection);
        w.Write(player.Spectator); w.Write(player.PublicAddress); w.Write(player.PublicPort);
        w.Write(player.PrivateAddress); w.Write(player.PrivatePort);
        w.Write((byte)0); w.Write(player.ClanMark);
        return w.Buffer;
    }
}
