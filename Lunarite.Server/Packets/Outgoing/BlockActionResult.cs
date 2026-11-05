using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class BlockActionResult : IPacketSerialize
{
    public byte Opcode;
    public byte Result;
    public byte[] Serialize()
    {
        using var writer = new PacketWriter(); writer.Write(Opcode); writer.Write(Result); return writer.Buffer;
    }
}
