using System;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

public sealed class SessionOpaqueToken : IPacketSerialize
{
    public byte[] Value = new byte[16];

    public byte[] Serialize()
    {
        if (Value.Length != 16) throw new InvalidOperationException("Session token must be exactly 16 bytes.");
        using var writer = new PacketWriter();
        writer.Write((byte)5);
        writer.Write(Value);
        return writer.Buffer;
    }
}
