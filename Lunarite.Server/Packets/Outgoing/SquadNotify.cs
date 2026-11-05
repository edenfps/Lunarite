using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server opcode 97 (OnSquadNotify) parses "dbb": PID, notification type, result.
public sealed class SquadNotify : IPacketSerialize
{
    public int Pid;
    public byte Type;
    public byte Result;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)97); writer.Write(Pid); writer.Write(Type); writer.Write(Result);
        return writer.Buffer;
    }
}
