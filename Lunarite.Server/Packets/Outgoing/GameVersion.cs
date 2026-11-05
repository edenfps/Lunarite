using Lunarite.Network;

namespace Lunarite.Packets.Outgoing;

// dddbbdd
public class GameVersion : IPacketSerialize
{
    public int ServerVersion;
    public int FullVersion;
    public int TrialVersion;

    private byte Unused = default;

    public byte CodeNum;

    public int Event;

    private int Unused2 = default;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write((byte)1);

        writer.Write(ServerVersion);
        writer.Write(FullVersion);
        writer.Write(TrialVersion);

        writer.Write(Unused);

        writer.Write(CodeNum);

        writer.Write(Event);

        writer.Write(Unused2);

        return writer.Buffer;
    }
}