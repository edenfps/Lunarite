using Lunarite.Network;
using Lunarite.Packets.Outgoing;

namespace Lunarite.Handlers;

[PacketHandler]
public static class TransportLeaveHandler
{
    public static bool HandlePacket(Client client, ref PacketReader reader)
    {
        // Native SendTransportLeave is op105 with no payload; server op119 is `bfffw`.
        if (reader.RemainingLength != 0) return false;
        int pid = Game.PidOf(client);
        bool riding = pid > 0 && Game.PlayerFor(pid).TryLeaveTransport();
        var position = (Game.PlayerFor(pid).Position ?? default);
        client.SendPacket(new TransportLeave
        {
            Answer = riding ? (byte)0 : (byte)1,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Yaw = position.Yaw
        });
        return true;
    }
}
