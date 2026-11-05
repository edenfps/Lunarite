using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native OnListenDiagnostics decodes opcode 63 as `db` (client PID, error code).
public sealed class ListenDiagnostics : IPacketSerialize
{
    public int ClientPid;
    public byte ErrorCode;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)63);
        writer.Write(ClientPid);
        writer.Write(ErrorCode);
        return writer.Buffer;
    }
}
