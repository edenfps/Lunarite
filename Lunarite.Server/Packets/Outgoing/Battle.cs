using System;

using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native server op31 OnBattle reads the answer byte first. A non-zero answer
// invokes the UnrealScript OnBattle error path without expecting endpoint data.
public sealed class Battle : IPacketSerialize
{
    public byte Answer;
    public int ServerIp;
    public ushort ServerPort;
    public byte[] Key = new byte[16];

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)31); writer.Write(Answer);
        if (Answer == 0)
        {
            if (Key.Length != 16) throw new InvalidOperationException("Battle key must be exactly 16 bytes.");
            writer.Write(ServerIp); writer.Write(ServerPort); writer.Write(Key);
        }
        return writer.Buffer;
    }
}
