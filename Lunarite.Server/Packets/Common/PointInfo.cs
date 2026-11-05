using Lunarite.Network;

namespace Lunarite.Packets.Common;

public class PointInfo : ISerializableType
{
    public int BP;
    public int LP;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(BP);
        writer.Write(LP);
    }
}