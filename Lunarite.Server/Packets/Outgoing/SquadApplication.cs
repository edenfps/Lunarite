using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// Server op104 OnSquadApplication parses `dsbwd`.
public sealed class SquadApplication : IPacketSerialize
{
    public int Pid;
    public string Name = "";
    public byte Style;
    public ushort Level;
    public int SquadKey;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        writer.Write((byte)104); writer.Write(Pid); writer.Write(Name); writer.Write(Style);
        writer.Write(Level); writer.Write(SquadKey);
        return writer.Buffer;
    }
}
