using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// CServerTcpLink incoming opcode 0: one-byte verification answer (0 = accepted).
public sealed class BattleBackendVerify : IPacketSerialize
{
    public byte Answer;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)0);
        writer.Write(Answer);
        return writer.Buffer;
    }
}
