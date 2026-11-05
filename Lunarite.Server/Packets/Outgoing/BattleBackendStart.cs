using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// CServerTcpLink incoming opcode 3, native format bbsbwbb.
public sealed class BattleBackendStart : IPacketSerialize
{
    public byte Version = 19;
    public byte MapIndex;
    public string MapUrl = "";
    public byte MaxPlayers;
    public ushort GoalScore;
    public byte TimeLimitMinutes;
    public byte WarmingUp;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)3);
        writer.Write(Version);
        writer.Write(MapIndex);
        writer.Write(MapUrl);
        writer.Write(MaxPlayers);
        writer.Write(GoalScore);
        writer.Write(TimeLimitMinutes);
        writer.Write(WarmingUp);
        return writer.Buffer;
    }
}
