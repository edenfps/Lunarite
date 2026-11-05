using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Native client handler sub_11113700 (server opcode 226): OnCityWarp(byte answer).
public sealed class CityWarp : IPacketSerialize
{
    public byte Result;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)226);
        writer.Write(Result);
        return writer.Buffer;
    }
}
