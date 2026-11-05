using System.Collections.Generic;
using System.Net;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// b
public class ServerList : IPacketSerialize
{
    public List<GameServer> Servers = [];

    // wdsbb
    public class GameServer : ISerializableType
    {
        public short Server;
        public required IPAddress IP;
        public required string Name;
        public byte Percet;
        public byte Camp;

        public void Serialize(PacketWriter writer)
        {
            writer.Write(Server);
            writer.Write(IP.GetAddressBytes());
            writer.Write(Name);
            writer.Write(Percet);
            writer.Write(Camp);
        }
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)8);

        writer.Write((byte)Servers.Count);

        foreach (var server in Servers)
            server.Serialize(writer);

        return writer.Buffer;
    }
}