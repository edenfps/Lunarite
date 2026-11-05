using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server -> client, opcode 14 (OnRestorePlayer @ 0x1110F0A0), format "bd".
public class RestorePlayer : IPacketSerialize
{
    public const byte Success = 0;
    public const byte Error = 1;

    public byte Answer;
    public int Pid;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)14);
        writer.Write(Answer);
        writer.Write(Pid);
        return writer.Buffer;
    }
}
