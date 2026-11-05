using Lunarite.Network;

namespace Lunarite.Packets.Common;

public class CharInfo : ISerializableType
{
    public int Level;

    public byte Style;
    public byte Rank;
    public byte Job;

    public int LunaritesFuel;
    public int Nick;
    public int Title;

    public void Serialize(PacketWriter writer)
    {
        // Native OnPlayerInfo reads the 20-byte wire record as
        // ushort level, byte style/rank/job, three bytes of alignment,
        // then the three dwords below.  A 32-bit level shifts style to
        // offset 4, where the client interprets it as Job instead.
        writer.Write((ushort)Level);
        writer.Write(Style);
        writer.Write(Rank);
        writer.Write(Job);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        writer.Write(LunaritesFuel);
        writer.Write(Nick);
        writer.Write(Title);
    }
}
