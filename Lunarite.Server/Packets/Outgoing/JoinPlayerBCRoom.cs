using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Room-server membership event. Native server op136 consumes
// dsbbbbbbbbbd: compact SCPlayerInfo identity/state, room ID, spectator flag,
// and clan mark. Channel observers use the later op147 full-endpoint form.
public sealed class JoinPlayerBCRoom : IPacketSerialize
{
    public byte RoomId;
    public SimulationPlayerWire Player;

    public byte[] Serialize()
    {
        using var w = new PacketWriter();
        var player = Player;
        w.Write((byte)136); w.Write(player.Pid); w.Write(player.Name); w.Write(player.Level); w.Write(player.Style);
        w.Write(player.Rank); w.Write(player.Squad); w.Write(player.Species); w.Write(player.State);
        w.Write(player.FullConnection); w.Write(RoomId); w.Write(player.Spectator); w.Write(player.ClanMark);
        return w.Buffer;
    }
}
