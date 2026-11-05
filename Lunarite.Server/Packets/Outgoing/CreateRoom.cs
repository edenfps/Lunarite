using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// ProcCreateSimulRoom (server op131) consumes bbbbbd unconditionally. The final
// dword is the listen/agent token; the June client parses but does not expose it.
public sealed class CreateRoom : IPacketSerialize
{
    public byte Result;
    public byte RoomId;
    public byte Species;
    public byte FullConnection = 1;
    public byte Spectator;
    public int AgentToken;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)131);
        writer.Write(Result);
        writer.Write(RoomId);
        writer.Write(Species);
        writer.Write(FullConnection);
        writer.Write(Spectator);
        writer.Write(AgentToken);
        return writer.Buffer;
    }
}
