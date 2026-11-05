using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class LFSActionResult : IPacketSerialize
{
    public byte Opcode;
    public byte Answer;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write(Opcode); writer.Write(Answer); return writer.Buffer;
    }
}
